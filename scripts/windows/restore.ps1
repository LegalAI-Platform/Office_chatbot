# Restores a backup and checks it (docs/runbooks/backup-restore.md). Never overwrites a live database or a file.
#   restore.ps1 list [office|n8n|chatwoot|files]
#   restore.ps1 <office|n8n|chatwoot> <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify] [--structure-only]
#   restore.ps1 files <latest|MANIFEST_NAME> [--into-live]
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
if ($args.Count -lt 1) {
    Fail "usage: restore.ps1 list | restore.ps1 <office|n8n|chatwoot> <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify] | restore.ps1 files <latest|MANIFEST> [--into-live]"
}
Invoke-Compose run --rm tools restore @args
