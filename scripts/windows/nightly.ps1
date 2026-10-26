# The nightly job (TASK 13, DECISIONS v1.18), every day at 03:00 from Task Scheduler (README, "Nightly job"):
#   1. backup         the three databases and the client files, encrypted
#   2. healthcheck    every check of the system
#   3. log-scan       the last day of the API's, n8n's, Chatwoot's and the databases' logs: no token, no full phone number
#   4. restore-drill  when the last drill that worked is 28 days old or more
# Every step records itself (hazem.ops_runs) and a failed one reaches the managers (the panel's bell and an
# e-mail). A failed step does not stop the next ones. Docker Desktop must be running.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
# _common stops on any error; here a step's stderr (a docker warning) must not stop the next steps.
$ErrorActionPreference = 'Continue'
$env:TEMP = Join-Path $Root '.local\tmp'; $env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
$failed = @()

function Step([string]$Name, [scriptblock]$Run) {
    Write-Host "==== $Name ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))"
    try { & $Run } catch { Write-Host "  $($_.Exception.Message)"; $script:failed += $Name; return }
    if ($LASTEXITCODE -ne 0) { $script:failed += $Name }
}

Step 'backup' { & docker compose run --rm tools backup }
Step 'healthcheck' { & docker compose run --rm --no-deps tools healthcheck }
Step 'log-scan' { & docker compose logs --no-color --since 24h api n8n chatwoot chatwoot-sidekiq postgres chatwoot-db 2>$null | & docker compose run --rm -T --no-deps tools log-scan }
Step 'restore-drill' { & docker compose run --rm tools restore-drill --if-due }

if ($failed.Count -gt 0) {
    Write-Host "Nightly job: FAILED in $($failed -join ', ') (the managers are told in the panel and by e-mail)."
    exit 1
}
Write-Host 'Nightly job: all steps worked.'
