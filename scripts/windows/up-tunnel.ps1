# Starts the core services plus a Cloudflare tunnel that exposes n8n ONLY.
# CLOUDFLARE_TUNNEL_TOKEN set   -> Named Tunnel (stable hostname)
# CLOUDFLARE_TUNNEL_TOKEN empty -> Quick Tunnel (new random URL every start)
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-NoPlaceholders

if (Get-EnvValue 'CLOUDFLARE_TUNNEL_TOKEN') {
    Invoke-Compose --profile tunnel-named up -d --wait
    Write-Host "Named Tunnel is up. Its public hostname is the one configured in the Cloudflare dashboard."
    exit 0
}

Invoke-Compose --profile tunnel up -d --wait
$url = $null
for ($i = 0; $i -lt 30 -and -not $url; $i++) {
    $logs = (& docker compose --profile tunnel logs tunnel 2>$null) -join "`n"
    $found = [regex]::Matches($logs, 'https://[a-z0-9-]+\.trycloudflare\.com')
    if ($found.Count -gt 0) { $url = $found[$found.Count - 1].Value } else { Start-Sleep -Seconds 2 }
}
if (-not $url) { Fail "could not read the tunnel URL. Check: docker compose --profile tunnel logs tunnel" }

$current = Get-EnvValue 'WEBHOOK_URL'
Write-Host ""
Write-Host "Tunnel URL: $url"
if ($current -ne "$url/") {
    Write-Host ""
    Write-Host "WEBHOOK_URL in .env is '$current'. To make n8n generate HTTPS webhook URLs:"
    Write-Host "  1. Set in .env:  WEBHOOK_URL=$url/   and   N8N_SECURE_COOKIE=true"
    Write-Host "  2. Restart n8n:  docker compose up -d n8n"
    Write-Host "This Quick Tunnel URL changes every time the tunnel restarts."
}
