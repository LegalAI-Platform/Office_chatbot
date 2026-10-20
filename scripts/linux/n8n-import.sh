#!/bin/bash
# Creates the credentials whose values are in .env (office-postgres-app, hazem-api,
# hazem-internal-send), imports n8n/workflows/*.json (credential placeholders resolved by
# name), publishes the internal workflows, and restarts n8n when a schedule or webhook
# was published. META-WA-INBOUND is published by tunnel-refresh.
source "$(dirname "$0")/_common.sh"
require_env_file
# Passed by name only (-e NAME), so the values never appear on a command line.
export OFFICE_APP_PASSWORD="$(env_value OFFICE_APP_PASSWORD)"
export HAZEM_API_TOKEN="$(env_value HAZEM_API_TOKEN)"
export N8N_INTERNAL_SEND_TOKEN="$(env_value N8N_INTERNAL_SEND_TOKEN)"
docker compose exec -T -e OFFICE_APP_PASSWORD -e HAZEM_API_TOKEN -e N8N_INTERNAL_SEND_TOKEN \
  -e N8N_RUNNERS_BROKER_PORT=5698 n8n sh /opt/office/n8n/scripts/create-local-credentials.sh
out="$(docker compose exec -T -e N8N_RUNNERS_BROKER_PORT=5698 n8n sh /opt/office/n8n/scripts/import-workflows.sh)"
echo "$out" | grep -v '^RESTART_N8N$' || true
if grep -q '^RESTART_N8N$' <<<"$out"; then
  echo "Restarting n8n so the new schedules and webhooks start..."
  docker compose restart n8n >/dev/null 2>&1
  docker compose up -d --wait n8n >/dev/null 2>&1
  echo "n8n is back."
fi
