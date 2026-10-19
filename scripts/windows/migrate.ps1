# Applies EF Core migrations and the Hangfire schema as office_migrator (Office.Cli).
#   Usage: migrate.ps1 [up|status]
param([ValidateSet('up', 'status')][string]$Command = 'up')
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
if ($Command -eq 'up') { Invoke-Compose run --rm cli migrate }
else { Invoke-Compose run --rm cli migrations-list }
