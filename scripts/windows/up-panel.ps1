# Starts the core services plus Appsmith (profile "panel").
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-NoPlaceholders
Invoke-Compose --profile panel up -d --wait
Invoke-Compose --profile panel ps
Write-Host "Appsmith: http://127.0.0.1:8080   n8n: http://127.0.0.1:5678"
