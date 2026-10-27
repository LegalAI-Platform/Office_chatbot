#!/bin/bash
# TEST ENVIRONMENT ONLY. Hazem forgets one number, to test a conversation from the start (migration 0025):
#   1. backup of the office and Chatwoot databases (exports/manual/, git-ignored)
#   2. Office.Cli forget-number: hazem.forget_wa_id (every row of the number) + its bookings' Google Calendar events
#   3. Chatwoot: the contact and its conversations (Chatwoot's own delete)
#   4. Redis: any key with the number
# The number stays in the allowed test numbers. Usage: scripts/linux/forget-number.sh 01012345678
source "$(dirname "$0")/_common.sh"
[[ $# -eq 1 ]] || die "usage: scripts/linux/forget-number.sh <number>"
[[ "$(env_value ENVIRONMENT)" == "test" ]] || die "forget-number runs in the test environment only (ENVIRONMENT=test in .env)"

# 01012345678, +20 10 1234 5678, 0020…, 201012345678 → 201012345678 (the CLI checks it again).
digits="$(printf '%s' "$1" | tr -cd '0-9')"
if [[ "$digits" == 0020* ]]; then digits="${digits:2}"
elif [[ "$digits" == 0* && ${#digits} -eq 11 ]]; then digits="20${digits:1}"; fi
[[ "$digits" =~ ^[0-9]{8,15}$ ]] || die "not a phone number: $1"
masked="${digits:0:4}******${digits: -2}"

echo "[1/4] Backup ($masked)"
stamp="$(date +%Y%m%d-%H%M%S)"
mkdir -p exports/manual
office="exports/manual/office-before-forget-$digits-$stamp.dump"
chatwoot="exports/manual/chatwoot-before-forget-$digits-$stamp.dump"
docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d office -Fc' > "$office" \
  || die "the office backup failed; nothing was deleted"
docker compose exec -T chatwoot-db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$chatwoot" \
  || die "the Chatwoot backup failed; nothing was deleted"
echo "      $office"
echo "      $chatwoot"

echo "[2/4] Hazem and the Google Calendar"
result="$(docker compose exec -T api dotnet /app/cli/Office.Cli.dll forget-number --wa "$digits")" || true
echo "$result"
grep -qE '"ok":[[:space:]]*true' <<<"$result" || die "forget-number failed (see above); Chatwoot and Redis were not touched"

echo "[3/4] Chatwoot"
chat="$(docker compose exec -T chatwoot bundle exec rails runner "
ids = Contact.where(identifier: '$digits').or(Contact.where(phone_number: '+$digits')).pluck(:id)
n = Conversation.where(contact_id: ids).count
Conversation.where(contact_id: ids).find_each(&:destroy!)
Contact.where(id: ids).find_each(&:destroy!)
puts %Q(contacts=#{ids.size} conversations=#{n})" 2>/dev/null | grep '^contacts=')" \
  || die "Chatwoot could not delete the contact (Hazem has already forgotten the number)"
echo "      $chat"

echo "[4/4] Redis"
keys="$(docker compose exec -T redis sh -c "redis-cli \${REDIS_PASSWORD:+-a \"\$REDIS_PASSWORD\"} --no-auth-warning --scan --pattern '*$digits*'")"
count=0
for k in $keys; do
  docker compose exec -T redis sh -c "redis-cli \${REDIS_PASSWORD:+-a \"\$REDIS_PASSWORD\"} --no-auth-warning del '$k'" >/dev/null
  count=$((count + 1))
done
echo "      keys deleted: $count"

echo "OK  Hazem forgot $masked. The next message from it starts from the welcome."
if grep -qE '"calendar_events_to_delete_by_hand":[[:space:]]*\[[[:space:]]*"' <<<"$result"; then
  echo "Some Google Calendar events could not be deleted: delete them by hand (the times are above)."
fi
