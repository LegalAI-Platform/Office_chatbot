# Full environment check: container health, then the checks in infra/tools/bin/healthcheck.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile

$failed = $false
Write-Host "== Container health"
foreach ($svc in 'postgres', 'redis', 'garage', 'n8n', 'api', 'web') {
    $health = (& docker compose ps --format '{{.Health}}' $svc 2>$null) -join ''
    if ($health -eq 'healthy') { Write-Host "PASS  $svc is healthy" }
    else { Write-Host "FAIL  $svc is '$health'"; $failed = $true }
}
foreach ($job in 'preflight', 'storage-init') {
    $code = (& docker compose ps -a --format '{{.ExitCode}}' $job 2>$null) -join ''
    if ($code -eq '0') { Write-Host "PASS  $job completed" }
    else { Write-Host "FAIL  $job exit code '$code'"; $failed = $true }
}
# v1.107: once monitoring.ps1 up started it (its Prometheus container exists), every part of it runs (on Docker Desktop
# without node-exporter, see monitoring.ps1).
Write-Host "== The monitoring's containers"
if ((& docker compose --profile monitoring ps -a --quiet prometheus 2>$null) -join '') {
    foreach ($svc in 'prometheus', 'alertmanager', 'grafana') {
        $health = (& docker compose --profile monitoring ps --format '{{.Health}}' $svc 2>$null) -join ''
        if ($health -eq 'healthy') { Write-Host "PASS  $svc is healthy" }
        else { Write-Host "FAIL  $svc is '$health'"; $failed = $true }
    }
    foreach ($svc in 'loki', 'alloy', 'docker-proxy', 'telegraf', 'postgres-exporter', 'chatwoot-db-exporter', 'redis-exporter',
                     'chatwoot-redis-exporter', 'blackbox') {
        $state = (& docker compose --profile monitoring ps --format '{{.State}}' $svc 2>$null) -join ''
        if ($state -eq 'running') { Write-Host "PASS  $svc is running" }
        else { Write-Host "FAIL  $svc is '$state'"; $failed = $true }
    }
} else {
    Write-Host "SKIP  the monitoring is not started (scripts\windows\monitoring.ps1 up)"
}
Write-Host "== Published ports (must all be 127.0.0.1)"
$ports = (& docker compose @AllProfiles ps --format '{{.Ports}}') -join "`n"
if ($ports -match '0\.0\.0\.0|\[::\]|:::') {
    Write-Host "FAIL  a port is published on all interfaces"; $failed = $true
} else {
    Write-Host "PASS  every published port is bound to 127.0.0.1"
}
Write-Host ""

& docker compose run --rm tools healthcheck
if ($LASTEXITCODE -ne 0) { $failed = $true }
if ($failed) { exit 1 }
