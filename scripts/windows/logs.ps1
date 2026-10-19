# Follows the logs of one service.   Usage: logs.ps1 <service> [lines]
param(
    [Parameter(Mandatory = $true)][string]$Service,
    [int]$Lines = 200
)
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
Invoke-Compose @AllProfiles logs -f --tail $Lines $Service
