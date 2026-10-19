# Restores a backup into a NEW database and verifies row counts. Never overwrites office or n8n.
#   restore.ps1 list [office|n8n]
#   restore.ps1 <office|n8n> <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify]
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
if ($args.Count -lt 1) {
    Fail "usage: restore.ps1 list | restore.ps1 <office|n8n> <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify]"
}
Invoke-Compose run --rm tools restore @args
