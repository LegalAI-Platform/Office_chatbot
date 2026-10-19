# Points the Meta webhook at the current Cloudflare Quick Tunnel URL (decision D-5).
# Run it every time you start testing, and after any restart of the tunnel, Docker or the PC.
#   1. n8n owner must exist           5. update WEBHOOK_URL / N8N_SECURE_COOKIE in .env (backup kept)
#   2. Meta credentials and settings  6. publish META-WA-INBOUND and restart n8n: on start-up the
#   3. start the tunnel, read its URL    WhatsApp Trigger registers the new URL at Meta
#   4. delete the old subscription     7. verify the callback URL Meta now has
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-NoPlaceholders

Write-Host "[1/7] n8n owner account"
if (-not (Test-N8nOwnerReady)) { Fail "create the n8n owner at http://127.0.0.1:5678 before exposing n8n" }

Write-Host "[2/7] Meta credentials and settings"
if (-not (Invoke-Workflow 'WaSubCheck000001')) {
    Fail "META-WA-SUBSCRIPTION-CHECK failed: check the credentials (docs/meta-setup.md part 3) and app.settings (whatsapp.*)"
}

Write-Host "[3/7] Quick Tunnel"
& docker compose --profile tunnel up -d --wait tunnel | Out-Null
$url = $null
for ($i = 0; $i -lt 30 -and -not $url; $i++) { $url = Get-TunnelUrl; if (-not $url) { Start-Sleep -Seconds 2 } }
if (-not $url) { Fail "could not read the tunnel URL (docker compose --profile tunnel logs tunnel)" }
$expected = "$url/webhook/$InboundWebhookId/webhook"
Write-Host "      $url"

$current = Get-MetaCallbackUrl
if ($current -eq $expected -and (Get-EnvValue 'WEBHOOK_URL') -eq "$url/") {
    Write-Host "Meta already points at $expected. Nothing to do."
    exit 0
}

Write-Host "[4/7] Delete the old Meta subscription"
if (-not (Invoke-Workflow 'WaSubDelete00001')) { Fail "could not delete the Meta subscription" }

Write-Host "[5/7] Update .env"
Copy-Item (Join-Path $Root '.env') (Join-Path $Root (".env.bak-" + (Get-Date -Format 'yyyyMMdd-HHmmss')))
Set-EnvValue 'WEBHOOK_URL' "$url/"
Set-EnvValue 'N8N_SECURE_COOKIE' 'true'

Write-Host "[6/7] Publish META-WA-INBOUND and restart n8n"
& docker compose exec -T n8n n8n publish:workflow "--id=$InboundWorkflowId" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "could not publish META-WA-INBOUND" }
& docker compose up -d --wait n8n | Out-Null
Start-Sleep -Seconds 5
$logs = (& docker compose logs n8n --since 2m 2>&1) -join "`n"
if ($logs -match 'problem activating|could not be activated|already has a webhook subscription') {
    Fail "META-WA-INBOUND did not activate (docker compose logs n8n; docs/runbooks/whatsapp.md)"
}

Write-Host "[7/7] Verify at Meta"
$now = Get-MetaCallbackUrl
if ($now -eq $expected) {
    Write-Host "OK  Meta webhook -> $now"
    Write-Host "Send a WhatsApp message to the test number and check app.wa_inbound_messages."
} else {
    Fail "Meta callback is '$now', expected '$expected'"
}
