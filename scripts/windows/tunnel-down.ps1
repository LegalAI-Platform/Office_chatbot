# Stops the Quick Tunnel. Meta cannot reach the webhook until tunnel-refresh runs again;
# events sent meanwhile are retried by Meta for a limited time, then lost.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
& docker compose --profile tunnel stop tunnel 2>$null | Out-Null
& docker compose --profile tunnel rm -f tunnel 2>$null | Out-Null
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $Root '.local\tunnel-url')
Write-Host "Tunnel stopped. Run tunnel-refresh before the next test session."
