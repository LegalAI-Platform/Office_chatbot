#!/bin/bash
# Stops exposing n8n: deletes the Meta subscription, unpublishes META-WA-INBOUND,
# stops the tunnel and returns n8n to http://127.0.0.1:5678.
# Meta cannot deliver any event until tunnel-refresh runs again.
source "$(dirname "$0")/_common.sh"
require_env_file

echo "[1/4] Delete the Meta subscription"
run_workflow WaSubDelete00001 | grep -E '"http_status"' \
  || echo "      WARNING: could not delete it (credentials missing?). Continuing."
echo "[2/4] Unpublish META-WA-INBOUND"
docker compose exec -T n8n n8n unpublish:workflow --id="$INBOUND_WORKFLOW_ID" >/dev/null 2>&1 || true
echo "[3/4] Stop the tunnel"
docker compose --profile tunnel stop tunnel >/dev/null 2>&1 || true
docker compose --profile tunnel rm -f tunnel >/dev/null 2>&1 || true
echo "[4/4] Local settings and restart n8n"
set_env_value WEBHOOK_URL "http://127.0.0.1:5678/"
set_env_value N8N_SECURE_COOKIE false
docker compose up -d --wait n8n >/dev/null
echo "Done. n8n is local only: http://127.0.0.1:5678"
