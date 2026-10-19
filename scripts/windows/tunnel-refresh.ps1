# Starts (or reuses) the Cloudflare Quick Tunnel and registers its URL as the Meta webhook.
# Run it at the start of every test session and after any restart of Docker or the machine:
# the Quick Tunnel URL changes every time the tunnel container restarts (DECISIONS v1.1, D-5).
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-NoPlaceholders
if ((Get-EnvValue 'WhatsApp__Enabled') -ne 'true') { Fail "WhatsApp__Enabled is not true in .env. Finish docs/meta-setup.md first." }

$stateDir = Join-Path $Root '.local'
New-Item -ItemType Directory -Force $stateDir | Out-Null
$state = Join-Path $stateDir 'tunnel-url'

& docker compose --profile tunnel up -d --wait api tunnel | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "could not start the API and the tunnel" }

$url = $null
for ($i = 0; $i -lt 30 -and -not $url; $i++) {
    $logs = (& docker compose --profile tunnel logs tunnel 2>$null) -join "`n"
    $found = [regex]::Matches($logs, 'https://[a-z0-9-]+\.trycloudflare\.com')
    if ($found.Count -gt 0) { $url = $found[$found.Count - 1].Value } else { Start-Sleep -Seconds 2 }
}
if (-not $url) { Fail "could not read the tunnel URL. Check: docker compose --profile tunnel logs tunnel" }
$callback = "$url/webhooks/whatsapp"

# Quick Tunnel DNS can take a few seconds; Meta verifies the URL immediately.
for ($i = 0; $i -lt 20; $i++) {
    try { if ((Invoke-WebRequest -UseBasicParsing -TimeoutSec 10 "$url/health/live").StatusCode -eq 200) { break } } catch { }
    Start-Sleep -Seconds 3
}

function Get-Callback {
    $json = (& docker compose run --rm cli wa-check 2>$null) -join "`n"
    try { return ($json | ConvertFrom-Json).callback_url } catch { return $null }
}

if ((Get-Callback) -eq $callback) {
    Write-Host "Webhook already points to $callback"
    Set-Content -Path $state -Value $url -Encoding ascii
    exit 0
}

Write-Host "Registering webhook: $callback"
& docker compose run --rm cli wa-subscribe-waba | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "WARN: could not confirm the WABA subscription (see: cli.ps1 wa-check)" }
& docker compose run --rm cli wa-subscribe --callback $callback
if ($LASTEXITCODE -ne 0) { Fail "Meta rejected the webhook. See docs/runbooks/whatsapp.md" }

$registered = Get-Callback
if ($registered -ne $callback) { Fail "Meta still reports '$registered' instead of '$callback'" }
Set-Content -Path $state -Value $url -Encoding ascii
Write-Host "OK  Meta webhook -> $callback  ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))"
