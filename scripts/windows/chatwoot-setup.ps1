# Sets up Chatwoot for the office (TASK 07b, DECISIONS T-24): the account, «حازم (النظام)», the
# Platform App and the API inbox with its signed webhook (infra/chatwoot/setup.rb). Safe to run
# again: it only fixes what changed (for example after new tokens in .env). Prints the ids, never a token.
# Then turns handoff.enabled on: «موظف» hands the client to the staff, who now answer from Chatwoot.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
foreach ($v in 'CHATWOOT_BOT_TOKEN', 'CHATWOOT_PLATFORM_TOKEN', 'CHATWOOT_WEBHOOK_TOKEN') {
    $value = Get-EnvValue $v
    if (-not $value) { Write-Error "$v is empty in .env (gen-secrets makes it)"; exit 1 }
    Set-Item -Path "env:$v" -Value $value
}
Invoke-Compose up -d --wait chatwoot
Invoke-Compose run --rm --no-deps -v "$(Join-Path $Root 'infra\chatwoot'):/setup:ro" `
    -e CHATWOOT_BOT_TOKEN -e CHATWOOT_PLATFORM_TOKEN -e CHATWOOT_WEBHOOK_TOKEN `
    chatwoot bundle exec rails runner /setup/setup.rb
$su = Get-EnvValue 'POSTGRES_SUPERUSER'
Invoke-Compose exec -T postgres psql -v ON_ERROR_STOP=1 -U $su -d office -c "UPDATE hazem.settings SET value = 'true', updated_by = 'chatwoot-setup' WHERE key = 'handoff.enabled'"
