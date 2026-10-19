# Runs dbmate migrations on the office database as office_migrator.
#   Usage: migrate.ps1 up|down|status
param([ValidateSet('up', 'down', 'status')][string]$Command = 'status')
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
Invoke-Compose run --rm migrate $Command
