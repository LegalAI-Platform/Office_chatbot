# End-to-end test of n8n <-> .NET API (DECISIONS v1.3) without Meta:
#   1. a text message -> n8n -> API (echo) -> n8n sends it (dry run) -> result back to the API
#   2. the same turn handed over again -> still exactly one reply
#   3. API stopped -> the message waits as pending; API started -> it is handled
#   4. API -> n8n internal send (CLI send-test), and both tokens refused when wrong
# Uses dummy numbers 20100000006x. Dry run and echo are switched off again at the end.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-NoPlaceholders
# docker writes progress to stderr; failures are judged by exit codes here, not by stderr.
$ErrorActionPreference = 'Continue'
$script:failed = $false

function Step([string[]]$StepArgs) {
    # The tools image is rebuilt on every run (pull_policy: build); hide that build log.
    & docker compose run --rm --no-deps tools hazem-e2e @StepArgs 2>&1 |
        ForEach-Object { "$_" } | Where-Object { $_ -notmatch '^#\d+ |Image office-tools|^\s*$' } | Write-Host
    return ($LASTEXITCODE -eq 0)
}
function Forward {
    if (-not (Invoke-Workflow 'WaFwdTurn0000001')) { Write-Host "FAIL  WA-FORWARD-TURN did not run"; $script:failed = $true }
}
function Check([string]$Name) { if (-not (Step @($Name))) { $script:failed = $true } }

try {
    Write-Host "==== 1. Message -> API -> reply"
    Step @('setup') | Out-Null
    Forward
    Check 'check-loop'

    Write-Host "==== 2. Same turn again"
    Step @('replay') | Out-Null
    Forward
    Check 'check-replay'

    Write-Host "==== 3. API down, then back"
    & docker compose stop api 2>&1 | Out-Null
    Step @('inject-b') | Out-Null
    Forward
    Check 'check-api-down'
    & docker compose up -d --wait api 2>&1 | Out-Null
    Forward
    Check 'check-recovered'

    Write-Host "==== 4. API -> n8n, and tokens"
    & docker compose exec -T api dotnet /app/cli/Office.Cli.dll send-test --to 201000000061 --text "e2e: API -> n8n" | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL  CLI send-test did not succeed"; $script:failed = $true }
    Check 'check-api-send'
    Check 'check-auth'
} finally {
    & docker compose up -d --wait api 2>&1 | Out-Null
    Step @('cleanup') | Out-Null
}

Write-Host ""
if ($script:failed) { Write-Host "hazem-e2e: some steps FAILED."; exit 1 }
Write-Host "hazem-e2e: all steps passed."
