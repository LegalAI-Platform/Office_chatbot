# Imports n8n/workflows/*.json into n8n (credential placeholders resolved by name)
# and publishes the internal sub-workflows. META-WA-INBOUND is published by tunnel-refresh.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
Invoke-Compose exec -T -e N8N_RUNNERS_BROKER_PORT=5698 n8n sh /opt/office/n8n/scripts/import-workflows.sh
