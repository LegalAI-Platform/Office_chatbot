# Shows every container, its health and its published ports.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
Invoke-Compose @AllProfiles ps -a --format 'table {{.Service}}\t{{.Status}}\t{{.Ports}}'
