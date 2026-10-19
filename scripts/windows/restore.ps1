# Restores a backup into a NEW database and verifies row counts. Never overwrites the live office database.
#   restore.ps1 list [office]
#   restore.ps1 office <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify]
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
if ($args.Count -lt 1) {
    Fail "usage: restore.ps1 list | restore.ps1 office <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify]"
}
Invoke-Compose run --rm tools restore @args
