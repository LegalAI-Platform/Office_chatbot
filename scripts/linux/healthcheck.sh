#!/bin/bash
# Full environment check: container health, then the checks in infra/tools/bin/healthcheck.
source "$(dirname "$0")/_common.sh"
require_env_file

failed=0
echo "== Container health"
for svc in postgres redis garage api; do
  health="$(docker compose ps --format '{{.Health}}' "$svc" 2>/dev/null || true)"
  if [[ "$health" == "healthy" ]]; then
    echo "PASS  $svc is healthy"
  else
    echo "FAIL  $svc is '${health:-not running}'"; failed=1
  fi
done
for job in preflight storage-init; do
  code="$(docker compose ps -a --format '{{.ExitCode}}' "$job" 2>/dev/null || true)"
  if [[ "$code" == "0" ]]; then echo "PASS  $job completed"; else echo "FAIL  $job exit code '${code:-missing}'"; failed=1; fi
done
echo "== Published ports (must all be 127.0.0.1)"
if docker compose "${ALL_PROFILES[@]}" ps --format '{{.Ports}}' | grep -E '0\.0\.0\.0|\[::\]|:::' ; then
  echo "FAIL  a port is published on all interfaces"; failed=1
else
  echo "PASS  every published port is bound to 127.0.0.1"
fi
echo

docker compose run --rm tools healthcheck || failed=1
exit $failed
