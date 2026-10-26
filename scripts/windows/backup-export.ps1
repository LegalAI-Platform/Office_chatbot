# A copy of the backups off this disk (TASK 13): the latest dump of every database and the client files, still
# encrypted, to a folder you name (an external drive, for example). The key is never copied: keep it apart.
#   backup-export.ps1 -To E:\OfficeBackups
param([Parameter(Mandatory = $true)][string]$To)
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
if (-not (Test-Path $To -PathType Container)) { Fail "folder not found: $To" }
$full = (Resolve-Path $To).Path
Invoke-Compose run --rm -v "${full}:/export" tools backup-export
