# Prints fresh random secrets. Copy them into .env yourself; this script never edits .env.
. (Join-Path $PSScriptRoot '_common.ps1')
& docker build -q -t office-tools:local infra/tools | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& docker run --rm office-tools:local gen-secrets
