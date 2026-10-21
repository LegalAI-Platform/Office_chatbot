# Creates the credentials whose values are in .env (office-postgres-app, hazem-api,
# hazem-internal-send, office-s3), imports n8n/workflows/*.json (credential placeholders resolved by
# name), publishes the internal workflows, and restarts n8n when a schedule or webhook
# was published. META-WA-INBOUND is published by tunnel-refresh.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
# Passed by name only (-e NAME), so the values never appear on a command line.
foreach ($v in 'OFFICE_APP_PASSWORD', 'HAZEM_API_TOKEN', 'N8N_INTERNAL_SEND_TOKEN',
              'S3_N8N_ACCESS_KEY_ID', 'S3_N8N_SECRET_ACCESS_KEY', 'S3_REGION') {
    Set-Item -Path "env:$v" -Value (Get-EnvValue $v)
}
Invoke-Compose exec -T -e OFFICE_APP_PASSWORD -e HAZEM_API_TOKEN -e N8N_INTERNAL_SEND_TOKEN `
    -e S3_N8N_ACCESS_KEY_ID -e S3_N8N_SECRET_ACCESS_KEY -e S3_REGION `
    -e N8N_RUNNERS_BROKER_PORT=5698 n8n sh /opt/office/n8n/scripts/create-local-credentials.sh
$out = @(& docker compose exec -T -e N8N_RUNNERS_BROKER_PORT=5698 n8n sh /opt/office/n8n/scripts/import-workflows.sh)
if ($LASTEXITCODE -ne 0) { $out | Write-Host; exit $LASTEXITCODE }
$out | Where-Object { $_ -ne 'RESTART_N8N' } | Write-Host
if ($out -contains 'RESTART_N8N') {
    Write-Host "Restarting n8n so the new schedules and webhooks start..."
    # docker writes progress to stderr; judge by the exit code only.
    $ErrorActionPreference = 'Continue'
    & docker compose restart n8n 2>&1 | Out-Null
    & docker compose up -d --wait n8n 2>&1 | Out-Null
    # One retry: right after a restart, --wait can fail once while dependencies settle.
    if ($LASTEXITCODE -ne 0) { & docker compose up -d --wait n8n 2>&1 | Out-Null }
    if ($LASTEXITCODE -ne 0) { Fail "n8n did not come back; check: docker compose logs n8n" }
    Write-Host "n8n is back."
}
