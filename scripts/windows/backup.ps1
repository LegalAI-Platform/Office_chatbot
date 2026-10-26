# The backup now (TASK 13): the office, n8n and Chatwoot databases and the client files, encrypted, to
# s3://office-backups (30 days). Every night it runs from nightly.ps1 (README, "Nightly job").
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
Invoke-Compose run --rm tools backup
