# TASK 02 tests that need no Meta account:
#   1. database rules with the fixtures in n8n/fixtures/whatsapp (as office_app)
#   2. the real n8n wiring through WA-TEST-HARNESS
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
$failed = $false

Write-Host "==== Database rules (fixtures)"
& docker compose run --rm tools wa-db-tests
if ($LASTEXITCODE -ne 0) { $failed = $true }

Write-Host ""
Write-Host "==== n8n workflows (WA-TEST-HARNESS)"
$result = Invoke-Workflow 'WaTestHarness001'
if ($result) {
    $result | & docker compose exec -T n8n node /opt/office/n8n/scripts/check-harness.js
    if ($LASTEXITCODE -ne 0) { $failed = $true }
} else {
    Write-Host "FAIL  WA-TEST-HARNESS did not run"; $failed = $true
}
if ($failed) { exit 1 }
