#!/bin/bash
# Exports the project's workflows from n8n back into n8n/workflows/ (credential ids
# replaced by CRED:<name>, volatile fields removed). Review the git diff afterwards.
source "$(dirname "$0")/_common.sh"
require_env_file
docker compose exec -T n8n sh /opt/office/n8n/scripts/export-workflows.sh
docker compose cp n8n:/tmp/office-export/. n8n/workflows/ >/dev/null
echo "Exported to n8n/workflows/. Check: git diff n8n/workflows"
