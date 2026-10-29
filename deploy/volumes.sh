#!/bin/bash
# Moves the stack's data, exactly as it is, from one machine to another (TASK 14: the owner's PC -> the server):
# the databases (office, n8n with its credentials, Chatwoot), the client files in storage, the panel's keys.
#   on the PC, the stack stopped:   deploy/volumes.sh export <folder>
#   on the server, before 1st run:  deploy/volumes.sh import <folder>
# Both machines need the same .env (the same passwords and keys open the same data) and the same image versions.
# The archives hold client data and n8n's credentials: copy them with scp only, and delete them once imported.
set -euo pipefail
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1

VOLUMES=(postgres_data n8n_data garage_meta garage_data redis_data api_keys chatwoot_db_data chatwoot_redis_data chatwoot_storage)
PROJECT="$(grep -E '^COMPOSE_PROJECT_NAME=' .env 2>/dev/null | cut -d= -f2)"; PROJECT="${PROJECT:-office}"
IMAGE=alpine:3.22.6 # the tools image's base

die() { echo "ERROR: $*" >&2; exit 1; }
[[ $# -eq 2 ]] || die "usage: deploy/volumes.sh export|import <folder>"
mode="$1"; dir="$(mkdir -p "$2" && cd "$2" && pwd)"
# Docker Desktop on Windows wants a Windows path for a bind mount
host_dir="$dir"; command -v cygpath >/dev/null 2>&1 && host_dir="$(cygpath -w "$dir")"

running="$(docker ps --filter "label=com.docker.compose.project=$PROJECT" --format '{{.Names}}')"
[[ -z "$running" ]] || die "stop the stack first (docker compose --profile tunnel down), still running: $(echo $running)"

case "$mode" in
  export)
    for v in "${VOLUMES[@]}"; do
      docker volume inspect "${PROJECT}_$v" >/dev/null 2>&1 || die "no volume ${PROJECT}_$v here"
      docker run --rm -v "${PROJECT}_$v:/v:ro" -v "$host_dir:/out" "$IMAGE" tar czf "/out/$v.tgz" -C /v .
      echo "exported $v ($(du -h "$dir/$v.tgz" | cut -f1))"
    done
    (cd "$dir" && sha256sum ./*.tgz > SHA256SUMS)
    echo "Done: $dir. Copy the folder to the server with scp, then delete it here."
    ;;
  import)
    (cd "$dir" && sha256sum -c --quiet SHA256SUMS) || die "an archive is damaged or missing (SHA256SUMS)"
    for v in "${VOLUMES[@]}"; do
      [[ -f "$dir/$v.tgz" ]] || die "missing $dir/$v.tgz"
      name="${PROJECT}_$v"
      if docker volume inspect "$name" >/dev/null 2>&1 \
         && [[ -n "$(docker run --rm -v "$name:/v:ro" "$IMAGE" ls -A /v)" ]]; then
        die "$name already holds data: refusing to overwrite it"
      fi
      # labelled like compose's own, so `docker compose` takes the volume as its own
      docker volume create --label com.docker.compose.project="$PROJECT" --label com.docker.compose.volume="$v" "$name" >/dev/null
      docker run --rm -v "$name:/v" -v "$host_dir:/in:ro" "$IMAGE" tar xzf "/in/$v.tgz" -C /v
      echo "imported $v"
    done
    echo "Done. Delete $dir now: it holds client data."
    ;;
  *) die "usage: deploy/volumes.sh export|import <folder>" ;;
esac
