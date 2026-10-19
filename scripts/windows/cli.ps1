# Runs an Office.Cli command in a container.
#   cli.ps1 migrate | migrations-list | wa-subscribe --callback URL | wa-subscribe-waba
#   cli.ps1 wa-check | templates-submit | templates-sync | send-test --to WA_ID [--text TEXT]
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
if ($args.Count -lt 1) { Fail "usage: cli.ps1 <command> [options]" }
Invoke-Compose run --rm cli @args
