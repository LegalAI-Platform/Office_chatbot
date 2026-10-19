#!/bin/bash
# Imports n8n/workflows/*.json into n8n (credential placeholders resolved by name)
# and publishes the internal sub-workflows. META-WA-INBOUND is published by tunnel-refresh.
source "$(dirname "$0")/_common.sh"
require_env_file
docker compose exec -T -e N8N_RUNNERS_BROKER_PORT=5698 n8n sh /opt/office/n8n/scripts/import-workflows.sh
