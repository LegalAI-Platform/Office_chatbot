# End-to-end test of n8n <-> .NET API without Meta or OpenAI (DECISIONS v1.3 + TASK 03):
#   1. a text message -> n8n -> API (echo) -> n8n sends it (dry run) -> result back to the API
#   2. the same turn handed over again -> still exactly one reply
#   3. API stopped -> the turn waits as pending; API started -> handled with the same turn_id
#   4. API -> n8n internal send (CLI send-test), and both tokens refused when wrong
#   5. two texts and a photo within 8 s -> one turn, one reply; the photo in office-sensitive
#   6. PDF, voice note (transcribed by the stub), too large, wrong sha256, forbidden type
#   7. a turn left processing while n8n restarts -> taken again, one reply
#   8. TASK 04: Hazem answers from approved texts (welcome, name, offer, B-20), a photo never
#      reaches the AI, a phone number is masked, and the AI stub down still gets an answer
#   9. TASK 05a: a listed client gets the neutral reply and a task for their staff, a blocked number
#      gets nothing (muted), a topic told first means no topic question after the name
#  10. TASK 07a: a staff account (created with the CLI, removed at the end) signs in to the panel
#      through the web container, finds that task under «مهامي», sees the client's words, starts
#      and finishes it; the panel never reaches n8n's endpoints
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

    Write-Host "==== 8. TASK 04: Hazem understands (AI stub) and answers from approved texts"
    Step @('hazem-setup') | Out-Null
    Forward
    Check 'check-hazem-welcome'
    Step @('hazem-name') | Out-Null
    Forward
    Check 'check-hazem-offer'
    Step @('hazem-legal') | Out-Null
    Forward
    Check 'check-hazem-legal'
    Step @('hazem-ai-down') | Out-Null
    Forward
    Check 'check-hazem-fallback'

    Write-Host "==== 9. TASK 05a: lists (B-14, B-04), the topic (B-25), the source (B-13)"
    Step @('hazem05-setup') | Out-Null
    Forward
    Check 'check-hazem05'
    Step @('hazem05-name') | Out-Null
    Forward
    Check 'check-hazem05-name'

    Write-Host "==== 10. TASK 07a: the panel (a staff account signs in through the web container)"
    # A throwaway password for the throwaway account: given to the CLI on stdin and to the check by
    # name (-e NAME), so it never appears on a command line.
    $env:E2E_PANEL_PASSWORD = -join (1..24 | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
    $env:E2E_PANEL_PASSWORD | & docker compose exec -T api dotnet /app/cli/Office.Cli.dll create-user `
        --name e2e-panel --display E2E --email e2e-panel@example.test --role staff --staff e2e-staff 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL  create-user e2e-panel (CLI)"; $script:failed = $true }
    & docker compose run --rm --no-deps -e E2E_PANEL_PASSWORD tools hazem-e2e check-panel 2>&1 |
        ForEach-Object { "$_" } | Where-Object { $_ -notmatch '^#\d+ |Image office-tools|^\s*$' } | Write-Host
    if ($LASTEXITCODE -ne 0) { $script:failed = $true }

    Write-Host "==== 11. TASK 08: booking from the buttons, the panel closes days, the lawyer's calendar (stub)"
    function PanelStep([string]$Name) {
        & docker compose run --rm --no-deps -e E2E_PANEL_PASSWORD tools hazem-e2e $Name 2>&1 |
            ForEach-Object { "$_" } | Where-Object { $_ -notmatch '^#\d+ |Image office-tools|^\s*$' } | Write-Host
        if ($LASTEXITCODE -ne 0) { $script:failed = $true }
    }
    Step @('hazem08-setup') | Out-Null
    Forward
    Check 'check-hazem08-welcome'
    Step @('hazem08-name') | Out-Null
    Forward
    Check 'check-hazem08-offer'
    PanelStep 'hazem08-close-today'
    Step @('hazem08-mode') | Out-Null
    Forward
    Check 'check-hazem08-times'
    Step @('hazem08-pick') | Out-Null
    Forward
    Check 'check-hazem08-booked'
    Write-Host "==== 12. TASK 09: the receipt confirms the hold, the staff review, the office cancels, the refund"
    Step @('hazem09-receipt') | Out-Null
    Forward
    Check 'check-hazem09-paid'
    PanelStep 'hazem09-review'
    Check 'check-hazem09-partial'
    PanelStep 'hazem08-office-closes'
    PanelStep 'check-hazem08-office-closed'
    Step @('hazem09-refund-press') | Out-Null
    Forward
    Check 'check-hazem09-refund-requested'
    PanelStep 'hazem09-refunded'
    Check 'check-hazem09-refunded'
    Write-Host "==== 13. TASK 10: a document review from the request to the file delivered on WhatsApp"
    # (b) only the lawyer or the manager prices: a throwaway lawyer account, same throwaway password.
    $env:E2E_PANEL_PASSWORD | & docker compose exec -T api dotnet /app/cli/Office.Cli.dll create-user `
        --name e2e-lawyer --display E2E-L --email e2e-lawyer@example.test --role lawyer 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL  create-user e2e-lawyer (CLI)"; $script:failed = $true }
    Step @('hazem10-setup') | Out-Null
    Forward
    Step @('hazem10-name') | Out-Null
    Forward
    Step @('hazem10-ask') | Out-Null
    Forward
    Check 'check-hazem10-order'
    Step @('hazem10-document') | Out-Null
    Forward
    Check 'check-hazem10-files'
    PanelStep 'hazem10-price'
    Check 'check-hazem10-priced'
    Step @('hazem10-receipt') | Out-Null
    Forward
    Check 'check-hazem10-receipt'
    PanelStep 'hazem10-deliver'
    Check 'check-hazem10-delivered'
    Write-Host "==== 14. TASK 11: an Urfi marriage contract from the questions to the lawyer's approval"
    Step @('hazem11-setup') | Out-Null
    Forward
    Step @('hazem11-say', '2', 'محمد أحمد علي حسن') | Out-Null
    Forward
    Step @('hazem11-say', '3', 'عايز أعمل جواز عرفي') | Out-Null
    Forward
    Check 'check-hazem11-ages'
    Step @('hazem11-say', '4', '30 و 25') | Out-Null
    Forward
    Check 'check-hazem11-wife-past'
    Step @('hazem11-press', '5', 'urf:wife_past:first') | Out-Null
    Forward
    Step @('hazem11-press', '6', 'urf:guardian:father') | Out-Null
    Forward
    Step @('hazem11-press', '7', 'urf:witnesses:office') | Out-Null
    Forward
    Check 'check-hazem11-times'
    Step @('hazem11-pick') | Out-Null
    Forward
    Check 'check-hazem11-held'
    Step @('hazem11-receipt') | Out-Null
    Forward
    Check 'check-hazem11-documents-asked'
    Step @('hazem11-document') | Out-Null
    Forward
    Check 'check-hazem11-documents'
    PanelStep 'hazem11-approve'
    Check 'check-hazem11-confirmed'
    Write-Host "==== 15. TASK 12: the reminders of that appointment, and «هحضر ✅»"
    Step @('hazem12-setup') | Out-Null
    Check 'check-hazem12-made'
    Step @('hazem12-due') | Out-Null
    Check 'check-hazem12-sent'
    Step @('hazem12-attend') | Out-Null
    Forward
    Check 'check-hazem12-attend'
    Remove-Item env:E2E_PANEL_PASSWORD -ErrorAction SilentlyContinue
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
