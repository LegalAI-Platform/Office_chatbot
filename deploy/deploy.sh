#!/bin/bash
# Puts one version of main on the server (TASK 14). Run on the server as «office», in /opt/office:
#   deploy/deploy.sh             the latest commit on main
#   deploy/deploy.sh 1a2b3c4     exactly that commit (also how to go back to an earlier one)
# Nothing is built here: the images are the ones the CI published for that commit (.github/workflows/images.yml),
# ghcr.io/legalai-platform/office-*:sha-<commit>. Steps: the files of that commit (compose, migrations, n8n workflows),
# the images named in .env, a backup, the migrations, the stack, the healthcheck. A failed healthcheck puts the
# previous version back (its images are still here); its database changes, if any, stay (migrations go forward only).
# The tunnel is not restarted, so its link (and Meta's webhook) stays the same.
set -euo pipefail
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1

die() { echo "ERROR: $*" >&2; exit 1; }
[[ -f .env ]] || die ".env not found in $PWD (docs/runbooks/server.md, step 4)"
[[ "$(id -un)" != "root" ]] || die "run as «office», not root"

set_env() {
  if grep -qE "^$1=" .env; then sed -i "s#^$1=.*#$1=$2#" .env; else echo "$1=$2" >> .env; fi
}
current() { grep -E '^OFFICE_API_IMAGE=' .env | sed -E 's/.*:sha-([0-9a-f]+)$/\1/' | grep -E '^[0-9a-f]{7}$' || true; }
point_at() { # the files and the images of one commit
  git checkout --quiet --detach "$1"
  local tag="sha-${1::7}"
  for img in api web tools; do set_env "OFFICE_${img^^}_IMAGE" "ghcr.io/legalai-platform/office-$img:$tag"; done
  set_env OFFICE_PULL_POLICY missing
}

echo "==== 1/6 The version"
git fetch --quiet origin main
target="$(git rev-parse --verify "${1:-origin/main}^{commit}")" || die "no such commit: ${1:-origin/main}"
previous="$(current)"
echo "now: ${previous:-nothing yet}   ->   ${target::7} $(git log -1 --format=%s "$target")"
if [[ "${target::7}" == "$previous" ]]; then echo "Already on that version."; exit 0; fi

echo "==== 2/6 Its images"
point_at "$target"
docker compose --profile tools pull --quiet api web tools preflight \
  || { [[ -n "$previous" ]] && point_at "$previous"; die "the images of ${target::7} are not on GHCR (did its Images run pass?) or the GHCR login is missing"; }

echo "==== 3/6 A backup first"
backed_up=0
if docker compose ps --status running --quiet postgres | grep -q .; then
  bash scripts/linux/backup.sh && backed_up=1
else
  echo "(the database is not running yet: the backup comes once it is up)"
fi

echo "==== 4/6 The migrations"
docker compose up -d --wait postgres
bash scripts/linux/migrate.sh up

echo "==== 5/6 The stack"
docker compose up -d --wait

echo "==== 6/6 The healthcheck"
# it wants a backup and a log scan from the last 26 hours (the nightly job's): a first deploy has had neither here
[[ $backed_up == 1 ]] || bash scripts/linux/backup.sh
docker compose logs --no-color --since 24h api n8n chatwoot chatwoot-sidekiq postgres chatwoot-db 2>/dev/null   | docker compose run --rm -T --no-deps tools log-scan
if bash scripts/linux/healthcheck.sh; then
  echo
  echo "Deployed ${target::7}."
  exit 0
fi
if [[ -n "$previous" ]]; then
  echo
  echo "The healthcheck failed: going back to $previous."
  point_at "$previous"
  docker compose up -d --wait
  die "${target::7} failed its healthcheck; $previous is running again"
fi
die "${target::7} failed its healthcheck (no earlier version to go back to)"
