# Starts the core services: postgres, redis, garage, storage-init, n8n.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-NoPlaceholders
Invoke-Compose up -d --wait
Invoke-Compose ps
Write-Host "n8n editor: http://127.0.0.1:5678"
