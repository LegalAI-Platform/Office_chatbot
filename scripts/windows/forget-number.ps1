# TEST ENVIRONMENT ONLY. Hazem forgets one number, to test a conversation from the start (migration 0025):
#   1. backup of the office and Chatwoot databases (exports\manual\, git-ignored)
#   2. Office.Cli forget-number: hazem.forget_wa_id (every row of the number) + its bookings' Google Calendar events
#   3. Chatwoot: the contact and its conversations (Chatwoot's own delete)
#   4. Redis: any key with the number
# The number stays in the allowed test numbers. Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\windows\forget-number.ps1 01012345678
param([Parameter(Mandatory = $true)][string]$Number)
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
# Docker writes its progress on stderr; Windows PowerShell 5.1 would stop on it. Exit codes are checked instead.
$ErrorActionPreference = 'Continue'

if ((Get-EnvValue 'ENVIRONMENT') -ne 'test') { Fail "forget-number runs in the test environment only (ENVIRONMENT=test in .env)" }

# 01012345678, +20 10 1234 5678, 0020..., 201012345678 -> 201012345678 (the CLI checks it again).
$digits = ($Number -replace '[^0-9]', '')
if ($digits.StartsWith('0020')) { $digits = $digits.Substring(2) }
elseif ($digits.StartsWith('0') -and $digits.Length -eq 11) { $digits = '20' + $digits.Substring(1) }
if ($digits -notmatch '^[0-9]{8,15}$') { Fail "not a phone number: $Number" }
$masked = $digits.Substring(0, 4) + '******' + $digits.Substring($digits.Length - 2)

Write-Host "[1/4] Backup ($masked)"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
New-Item -ItemType Directory -Force (Join-Path $Root 'exports\manual') | Out-Null
$office = "exports\manual\office-before-forget-$digits-$stamp.dump"
$chatwoot = "exports\manual\chatwoot-before-forget-$digits-$stamp.dump"
# cmd keeps the dump's bytes as they are (a PowerShell 5.1 redirect would re-encode them).
cmd /c "docker compose exec -T postgres sh -c ""pg_dump -U `$POSTGRES_USER -d office -Fc"" > $office"
if ($LASTEXITCODE -ne 0) { Fail "the office backup failed; nothing was deleted" }
cmd /c "docker compose exec -T chatwoot-db sh -c ""pg_dump -U `$POSTGRES_USER -d `$POSTGRES_DB -Fc"" > $chatwoot"
if ($LASTEXITCODE -ne 0) { Fail "the Chatwoot backup failed; nothing was deleted" }
Write-Host "      $office"
Write-Host "      $chatwoot"

Write-Host "[2/4] Hazem and the Google Calendar"
$result = (& docker compose exec -T api dotnet /app/cli/Office.Cli.dll forget-number --wa $digits) -join "`n"
Write-Host $result
if ($LASTEXITCODE -ne 0 -or $result -notmatch '"ok":\s*true') { Fail "forget-number failed (see above); Chatwoot and Redis were not touched" }

Write-Host "[3/4] Chatwoot"
$ruby = "ids = Contact.where(identifier: '$digits').or(Contact.where(phone_number: '+$digits')).pluck(:id); " +
        "n = Conversation.where(contact_id: ids).count; Conversation.where(contact_id: ids).find_each(&:destroy!); " +
        "Contact.where(id: ids).find_each(&:destroy!); puts %Q(contacts=#{ids.size} conversations=#{n})"
$chat = (& docker compose exec -T chatwoot bundle exec rails runner $ruby) | Where-Object { $_ -match '^contacts=' }
if ($LASTEXITCODE -ne 0) { Fail "Chatwoot could not delete the contact (Hazem has already forgotten the number)" }
Write-Host "      $chat"

Write-Host "[4/4] Redis"
$keys = (& docker compose exec -T redis sh -c "redis-cli `${REDIS_PASSWORD:+-a `"`$REDIS_PASSWORD`"} --no-auth-warning --scan --pattern '*$digits*'") | Where-Object { $_ }
foreach ($k in $keys) {
    & docker compose exec -T redis sh -c "redis-cli `${REDIS_PASSWORD:+-a `"`$REDIS_PASSWORD`"} --no-auth-warning del '$k'" | Out-Null
}
Write-Host "      keys deleted: $(@($keys).Count)"

Write-Host "OK  Hazem forgot $masked. The next message from it starts from the welcome."
if ($result -match '"calendar_events_to_delete_by_hand":\s*\[\s*"') {
    Write-Host "Some Google Calendar events could not be deleted: delete them by hand (the times are above)." -ForegroundColor Yellow
}
