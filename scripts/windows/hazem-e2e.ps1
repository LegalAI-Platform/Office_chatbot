# End-to-end test of n8n <-> .NET API without Meta or OpenAI (DECISIONS v1.3 + TASK 03):
#   1. a text message -> n8n -> API (echo) -> n8n sends it (dry run) -> result back to the API
#   2. the same turn handed over again -> still exactly one reply
#   3. API stopped -> the turn waits as pending; API started -> handled with the same turn_id
#   4. API -> n8n internal send (CLI send-test), and both tokens refused when wrong
#   5. two texts and a photo within 8 s -> one turn, one reply; the photo in office-sensitive
#   6. PDF, voice note (transcribed by the stub), too large, wrong sha256, forbidden type
#   7. a turn left processing while n8n restarts -> taken again, one reply
# Meta and OpenAI are the meta-stub container (profile tests). If meta-whatsapp-token or
# openai-transcription do not exist, temporary ones (fixed test ids) are created and removed
# again; existing (real) credentials are never touched. Everything is restored at the end.
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
function Get-ImportWarning {
    # Runs n8n-import; returns its "credentials not found" line (or "none").
    $out = @(& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'n8n-import.ps1') 2>&1 | ForEach-Object { "$_" })
    $line = $out | Where-Object { $_ -like 'WARNING: credentials not found*' } | Select-Object -First 1
    if ($line) { return $line }
    return 'none'
}

Write-Host "==== 0. Preparation: stub, temporary credentials"
$before = Get-ImportWarning
Write-Host "      before: $before"
try {
    & docker compose --profile tests up -d --wait meta-stub 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "meta-stub did not start" }
    & docker compose exec -T n8n sh /opt/office/n8n/scripts/e2e-credentials.sh add
    Get-ImportWarning | Out-Null

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

    Write-Host "==== 5. Two texts and a photo within 8 seconds"
    Step @('group-c') | Out-Null
    Start-Sleep -Seconds 10   # the 8-second quiet period
    Forward
    Check 'check-group'

    Write-Host "==== 6. PDF, voice note, too large, wrong sha256, forbidden type"
    Step @('media-d') | Out-Null
    Start-Sleep -Seconds 10
    Forward
    Check 'check-media'

    Write-Host "==== 7. Turn stuck while n8n restarts"
    Step @('stale-e') | Out-Null
    & docker compose restart n8n 2>&1 | Out-Null
    & docker compose up -d --wait n8n 2>&1 | Out-Null
    Start-Sleep -Seconds 22   # whatsapp.turn_stale_seconds is 20 during this step
    Forward
    Check 'check-stale'
} catch {
    Write-Host "FAIL  $_"; $script:failed = $true
} finally {
    & docker compose up -d --wait api 2>&1 | Out-Null
    Step @('cleanup') | Out-Null
    & docker compose --profile tests rm -sf meta-stub 2>&1 | Out-Null
    Step @('remove-test-credentials') | Out-Null
    Write-Host "==== Restore n8n (re-import without the test credentials)"
    $after = Get-ImportWarning
    if ($after -eq $before) { Write-Host "PASS  n8n credentials are exactly as before the test" }
    else { Write-Host "FAIL  n8n credentials changed (before: $before / after: $after)"; $script:failed = $true }
    Check 'check-restored'
}

Write-Host ""
if ($script:failed) { Write-Host "hazem-e2e: some steps FAILED."; exit 1 }
Write-Host "hazem-e2e: all steps passed."
