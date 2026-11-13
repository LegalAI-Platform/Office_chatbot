#!/bin/bash
# Full environment check: container health, then the checks in infra/tools/bin/healthcheck.
source "$(dirname "$0")/_common.sh"
require_env_file

failed=0
echo "== Container health"
for svc in postgres redis garage n8n api web; do
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
# v1.107: once monitoring.sh up started it (its Prometheus container exists), every part of it runs.
echo "== The monitoring's containers"
if docker compose --profile monitoring ps -a --quiet prometheus 2>/dev/null | grep -q .; then
  for svc in prometheus alertmanager grafana; do
    health="$(docker compose --profile monitoring ps --format '{{.Health}}' "$svc" 2>/dev/null || true)"
    if [[ "$health" == "healthy" ]]; then echo "PASS  $svc is healthy"; else echo "FAIL  $svc is '${health:-not running}'"; failed=1; fi
  done
  for svc in loki alloy docker-proxy node-exporter telegraf postgres-exporter chatwoot-db-exporter redis-exporter chatwoot-redis-exporter blackbox; do
    state="$(docker compose --profile monitoring ps --format '{{.State}}' "$svc" 2>/dev/null || true)"
    if [[ "$state" == "running" ]]; then echo "PASS  $svc is running"; else echo "FAIL  $svc is '${state:-not running}'"; failed=1; fi
  done
else
  echo "SKIP  the monitoring is not started (scripts/linux/monitoring.sh up)"
fi
echo "== Published ports (must all be 127.0.0.1)"
if docker compose "${ALL_PROFILES[@]}" ps --format '{{.Ports}}' | grep -E '0\.0\.0\.0|\[::\]|:::' ; then
  echo "FAIL  a port is published on all interfaces"; failed=1
else
  echo "PASS  every published port is bound to 127.0.0.1"
fi
echo

docker compose run --rm tools healthcheck || failed=1
exit $failed
