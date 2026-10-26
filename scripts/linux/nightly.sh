#!/bin/bash
# The nightly job (TASK 13, DECISIONS v1.18), every day at 03:00 (cron, README "Nightly job"):
#   backup, healthcheck, log-scan, and restore-drill when the last drill that worked is 28 days old or more.
# Every step records itself (hazem.ops_runs) and a failed one reaches the managers (the bell and an e-mail).
#   0 3 * * * /path/to/repo/scripts/linux/nightly.sh >> /var/log/office-nightly.log 2>&1
source "$(dirname "$0")/_common.sh"
require_env_file
failed=()
step() {
  local name="$1"; shift
  echo "==== $name ($(date '+%Y-%m-%d %H:%M'))"
  "$@" || failed+=("$name")
}
step backup docker compose run --rm tools backup
step healthcheck docker compose run --rm --no-deps tools healthcheck
step log-scan bash -c 'docker compose logs --no-color --since 24h api n8n 2>/dev/null | docker compose run --rm -T --no-deps tools log-scan'
step restore-drill docker compose run --rm tools restore-drill --if-due
if [[ ${#failed[@]} -gt 0 ]]; then
  echo "Nightly job: FAILED in ${failed[*]} (the managers are told in the panel and by e-mail)."
  exit 1
fi
echo "Nightly job: all steps worked."
