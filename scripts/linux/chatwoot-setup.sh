#!/bin/bash
# Sets up Chatwoot for the office (TASK 07b, DECISIONS T-24): the account, «حازم (النظام)», the
# Platform App and the API inbox with its signed webhook (infra/chatwoot/setup.rb). Safe to run
# again: it only fixes what changed (for example after new tokens in .env). Prints the ids, never a token.
# Then turns handoff.enabled on: «موظف» hands the client to the staff, who now answer from Chatwoot.
source "$(dirname "$0")/_common.sh"
require_env_file
for v in CHATWOOT_BOT_TOKEN CHATWOOT_PLATFORM_TOKEN CHATWOOT_WEBHOOK_TOKEN; do
  value="$(env_value "$v")"
  [[ -n "$value" ]] || die "$v is empty in .env (gen-secrets makes it)"
  export "$v=$value"
done
docker compose up -d --wait chatwoot
docker compose run --rm --no-deps -v "$ROOT/infra/chatwoot:/setup:ro" \
  -e CHATWOOT_BOT_TOKEN -e CHATWOOT_PLATFORM_TOKEN -e CHATWOOT_WEBHOOK_TOKEN \
  chatwoot bundle exec rails runner /setup/setup.rb
docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U "$(env_value POSTGRES_SUPERUSER)" -d office \
  -c "UPDATE hazem.settings SET value = 'true', updated_by = 'chatwoot-setup' WHERE key = 'handoff.enabled'"
