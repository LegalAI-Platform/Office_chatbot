# The monitoring (v1.107, docs/runbooks/monitoring.md): profile "monitoring". The same commands as scripts/linux/monitoring.sh.
# On Docker Desktop it starts without node-exporter: Docker Desktop cannot mount the machine's / the way it needs (rslave),
# and the "machine" there is Docker Desktop's own VM anyway. The server (Linux) runs all of it.
#   .\scripts\windows\monitoring.ps1 up | down | status | test-email | check | grafana-password
param([string]$Command = '')
. (Join-Path $PSScriptRoot '_common.ps1')

$Services = @('monitoring-init', 'prometheus', 'alertmanager', 'loki', 'alloy', 'docker-proxy', 'telegraf',
              'postgres-exporter', 'chatwoot-db-exporter', 'redis-exporter', 'chatwoot-redis-exporter', 'blackbox', 'grafana')
# They read a config file of this repo: compose does not see a changed file, so `up` recreates them.
$Configured = @('prometheus', 'alertmanager', 'loki', 'alloy', 'telegraf', 'blackbox', 'grafana')

switch ($Command) {
    'up' {
        Assert-EnvFile
        Invoke-Compose --profile monitoring up -d --wait @Services
        Invoke-Compose --profile monitoring up -d --wait --no-deps --force-recreate @Configured
        $port = Get-EnvValue 'GRAFANA_PORT'
        if (-not $port) { $port = '3300' }
        Write-Host ""
        Write-Host "The monitoring is up (without node-exporter on Docker Desktop). Grafana: http://127.0.0.1:$port"
    }
    'down' {
        Assert-EnvFile
        Invoke-Compose --profile monitoring stop @Services node-exporter
        Invoke-Compose --profile monitoring rm -f @Services node-exporter
        Write-Host "The monitoring is stopped; its data is kept (monitoring.ps1 up starts it again)."
    }
    'status' {
        Assert-EnvFile
        Invoke-Compose --profile monitoring ps -a --format 'table {{.Service}}\t{{.Status}}\t{{.Ports}}' @Services
    }
    { $_ -in 'test-email', 'check', 'grafana-password' } {
        # These pass quotes into the containers (amtool's labels, a shell line), which Windows PowerShell 5.1 and 7 quote
        # differently: they run as the Linux script, through Git Bash (the repo's Windows setup has it, CLAUDE.md).
        Assert-EnvFile
        $env:MSYS_NO_PATHCONV = '1'
        & bash scripts/linux/monitoring.sh $Command
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    default {
        Write-Host "Usage: .\scripts\windows\monitoring.ps1 up | down | status | test-email | check | grafana-password"
        exit 2
    }
}
