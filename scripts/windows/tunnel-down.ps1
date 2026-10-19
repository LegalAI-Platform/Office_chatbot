# Stops exposing n8n: deletes the Meta subscription, unpublishes META-WA-INBOUND,
# stops the tunnel and returns n8n to http://127.0.0.1:5678.
# Meta cannot deliver any event until tunnel-refresh runs again.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile

Write-Host "[1/4] Delete the Meta subscription"
if (-not (Invoke-Workflow 'WaSubDelete00001')) { Write-Host "      WARNING: could not delete it (credentials missing?). Continuing." }
Write-Host "[2/4] Unpublish META-WA-INBOUND"
& docker compose exec -T n8n n8n unpublish:workflow "--id=$InboundWorkflowId" 2>&1 | Out-Null
Write-Host "[3/4] Stop the tunnel"
& docker compose --profile tunnel stop tunnel 2>&1 | Out-Null
& docker compose --profile tunnel rm -f tunnel 2>&1 | Out-Null
Write-Host "[4/4] Local settings and restart n8n"
Set-EnvValue 'WEBHOOK_URL' 'http://127.0.0.1:5678/'
Set-EnvValue 'N8N_SECURE_COOKIE' 'false'
Invoke-Compose up -d --wait n8n | Out-Null
Write-Host "Done. n8n is local only: http://127.0.0.1:5678"
