# DESTRUCTIVE: stops everything and deletes ALL data volumes (database, files).
# Only allowed when ENVIRONMENT=test, and only after typing RESET.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile

$envName = Get-EnvValue 'ENVIRONMENT'
if ($envName -ne 'test') { Fail "reset is only allowed when ENVIRONMENT=test (current: '$envName')" }

Write-Host "This deletes every volume: PostgreSQL (office), Redis and object storage data."
$answer = Read-Host "Type RESET to continue"
if ($answer -cne 'RESET') { Fail "aborted (you typed '$answer')" }

Invoke-Compose @AllProfiles down --volumes --remove-orphans
Write-Host "All volumes deleted. Start again with scripts\windows\up.ps1 (migrations run automatically)."
