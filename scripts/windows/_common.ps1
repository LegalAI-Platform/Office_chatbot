# Shared helpers for scripts\windows\*.ps1. Dot-sourced, not executed.
# Works on Windows PowerShell 5.1 and PowerShell 7.
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Root

$AllProfiles = @('--profile', 'tunnel', '--profile', 'tunnel-named', '--profile', 'tools')

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
    $line = Get-Content (Join-Path $Root '.env') | Where-Object { $_ -match "^$Name=" } | Select-Object -Last 1
    if ($null -eq $line) { return '' }
    return $line.Substring($Name.Length + 1)
}

function Assert-NoPlaceholders {
    Assert-EnvFile
    $bad = Get-Content (Join-Path $Root '.env') |
        Where-Object { $_ -notmatch '^\s*#' -and $_ -match 'CHANGE_ME' } |
        ForEach-Object { ($_ -split '=', 2)[0] }
    if ($bad) {
        Write-Host "ERROR: .env still contains CHANGE_ME values:" -ForegroundColor Red
        $bad | ForEach-Object { Write-Host "  $_" }
        Fail "replace them (scripts\windows\gen-secrets.ps1 prints fresh values)"
    }
}
