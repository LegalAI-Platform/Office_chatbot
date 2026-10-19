# Starts the core services: postgres, redis, garage, storage-init, api.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-NoPlaceholders
Invoke-Compose up -d --build --wait
Invoke-Compose ps
Write-Host "API: http://127.0.0.1:8081/health/ready"
