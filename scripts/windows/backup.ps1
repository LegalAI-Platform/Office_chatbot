# Dumps the office database to s3://office-backups (kept 30 days).
# Daily schedule: see README.md, section "Daily backup" (Task Scheduler).
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
Invoke-Compose run --rm tools backup
