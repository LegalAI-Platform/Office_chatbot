# Shared helpers for scripts\windows\*.ps1. Dot-sourced, not executed.
# Works on Windows PowerShell 5.1 and PowerShell 7.
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Root

$AllProfiles = @('--profile', 'tunnel', '--profile', 'tunnel-named', '--profile', 'tools', '--profile', 'tests')

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Invoke-Compose {
    # Runs docker compose with the given arguments and stops on a non-zero exit code.
    & docker compose @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Assert-EnvFile {
    if (-not (Test-Path (Join-Path $Root '.env'))) {
        Fail ".env not found. Run: Copy-Item .env.example .env  (then fill it, see README.md)"
    }
}

function Get-EnvValue([string]$Name) {
    $line = Get-Content (Join-Path $Root '.env') -Encoding UTF8 | Where-Object { $_ -match "^$Name=" } | Select-Object -Last 1
    if ($null -eq $line) { return '' }
    return $line.Substring($Name.Length + 1)
}

# TASK 02: WhatsApp inbound workflow and its fixed webhook id (see n8n/workflows/META-WA-INBOUND.json).
$InboundWorkflowId = 'WaInbound0000001'
$InboundWebhookId = '3c1f0b6e-6a52-4f0e-9a51-0e0c0a5ea001'

function Invoke-Workflow([string]$Id) {
    # Runs a workflow with the n8n CLI inside the container; returns the last node's JSON (string) or $null.
    $out = (& docker compose exec -T n8n sh /opt/office/n8n/scripts/run-workflow.sh $Id 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { Write-Host $out; return $null }
    return $out
}

function Set-EnvValue([string]$Name, [string]$Value) {
    $path = Join-Path $Root '.env'
    # -Encoding UTF8: Windows PowerShell 5.1 would read the file in the ANSI code page and garble
    # every non-ASCII value (an Arabic SMTP_FROM_NAME) each time a script rewrites .env.
    $lines = @(Get-Content $path -Encoding UTF8)
    $found = $false
    $lines = $lines | ForEach-Object {
        if ($_ -match "^$Name=") { $found = $true; "$Name=$Value" } else { $_ }
    }
    if (-not $found) { $lines += "$Name=$Value" }
    # LF line endings and no BOM, like the original file.
    [IO.File]::WriteAllText($path, (($lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
}

function Test-N8nOwnerReady {
    try {
        $s = Invoke-RestMethod -UseBasicParsing -TimeoutSec 10 http://127.0.0.1:5678/rest/settings
        return ($s.data.userManagement.showSetupOnFirstLoad -eq $false)
    } catch { return $false }
}

function Get-TunnelUrl {
    $logs = (& docker compose --profile tunnel logs tunnel 2>$null) -join "`n"
    $found = [regex]::Matches($logs, 'https://[a-z0-9-]+\.trycloudflare\.com')
    if ($found.Count -gt 0) { return $found[$found.Count - 1].Value }
    return $null
}

function Get-MetaCallbackUrl {
    $json = Invoke-Workflow 'WaSubCheck000001'
    if (-not $json) { return $null }
    $sub = ($json | ConvertFrom-Json).whatsapp_subscription
    if ($sub) { return $sub.callback_url }
    return ''
}

function Assert-NoPlaceholders {
    Assert-EnvFile
    $bad = Get-Content (Join-Path $Root '.env') -Encoding UTF8 |
        Where-Object { $_ -notmatch '^\s*#' -and $_ -match 'CHANGE_ME' } |
        ForEach-Object { ($_ -split '=', 2)[0] }
    if ($bad) {
        Write-Host "ERROR: .env still contains CHANGE_ME values:" -ForegroundColor Red
        $bad | ForEach-Object { Write-Host "  $_" }
        Fail "replace them (scripts\windows\gen-secrets.ps1 prints fresh values)"
    }
}
