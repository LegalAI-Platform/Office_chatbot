#!/bin/bash
# Stops the Quick Tunnel. Meta cannot reach the webhook until tunnel-refresh runs again;
# events sent meanwhile are retried by Meta for a limited time, then lost.
source "$(dirname "$0")/_common.sh"
require_env_file
docker compose --profile tunnel stop tunnel >/dev/null 2>&1 || true
docker compose --profile tunnel rm -f tunnel >/dev/null 2>&1 || true
rm -f .local/tunnel-url
echo "Tunnel stopped. Run tunnel-refresh before the next test session."
