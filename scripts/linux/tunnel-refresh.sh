#!/bin/bash
# Points the Meta webhook at the current Cloudflare Quick Tunnel URL (decision D-5).
# Run it every time you start testing, and after any restart of the tunnel, Docker or the PC.
#   1. n8n owner must exist           5. update WEBHOOK_URL / N8N_SECURE_COOKIE in .env (backup kept)
#   2. Meta credentials and settings  6. publish META-WA-INBOUND and restart n8n: on start-up the
#   3. start the tunnel, read its URL    WhatsApp Trigger registers the new URL at Meta
#   4. delete the old subscription     7. verify the callback URL Meta now has
source "$(dirname "$0")/_common.sh"
check_no_placeholders

echo "[1/7] n8n owner account"
n8n_owner_ready || die "create the n8n owner at http://127.0.0.1:5678 before exposing n8n"

echo "[2/7] Meta credentials and settings"
run_workflow WaSubCheck000001 >/dev/null \
  || die "META-WA-SUBSCRIPTION-CHECK failed: check the credentials (docs/meta-setup.md part 3) and app.settings (whatsapp.*)"

echo "[3/7] Quick Tunnel"
docker compose --profile tunnel up -d --wait tunnel >/dev/null
url=""
for _ in $(seq 1 30); do url="$(tunnel_url_from_logs)"; [[ -n "$url" ]] && break; sleep 2; done
[[ -n "$url" ]] || die "could not read the tunnel URL (docker compose --profile tunnel logs tunnel)"
expected="$url/webhook/$INBOUND_WEBHOOK_ID/webhook"
echo "      $url"

# A new Quick Tunnel hostname takes a little while to answer. Meta checks the callback the moment
# n8n registers it, and a failed check is not retried by n8n, so wait until the link answers twice.
echo "      waiting for the link to answer..."
ok=0
for _ in $(seq 1 40); do
  if [[ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$url/healthz" || true)" == "200" ]]; then
    ok=$((ok + 1)); [[ $ok -ge 2 ]] && break
  else
    ok=0
  fi
  sleep 3
done
[[ $ok -ge 2 ]] || die "the tunnel URL does not answer yet ($url/healthz); run tunnel-refresh again in a minute"

current="$(run_workflow WaSubCheck000001 | grep -oE '"callback_url": *"[^"]*"' | sed -E 's/.*"([^"]*)"$/\1/' || true)"
if [[ "$current" == "$expected" && "$(env_value WEBHOOK_URL)" == "$url/" ]]; then
  echo "Meta already points at $expected. Nothing to do."
  exit 0
fi

echo "[4/7] Delete the old Meta subscription"
run_workflow WaSubDelete00001 | grep -E '"http_status"' || die "could not delete the Meta subscription"

echo "[5/7] Update .env"
cp .env ".env.bak-$(date +%Y%m%d-%H%M%S)"
set_env_value WEBHOOK_URL "$url/"
set_env_value N8N_SECURE_COOKIE true

echo "[6/7] Publish META-WA-INBOUND and restart n8n"
docker compose exec -T n8n n8n publish:workflow --id="$INBOUND_WORKFLOW_ID" >/dev/null 2>&1 \
  || die "could not publish META-WA-INBOUND"
docker compose up -d --wait n8n >/dev/null
sleep 5
if docker compose logs n8n --since 2m 2>&1 | grep -iE 'problem activating|could not be activated|already has a webhook subscription'; then
  die "META-WA-INBOUND did not activate (see the lines above and docs/runbooks/whatsapp.md)"
fi

echo "[7/7] Verify at Meta"
now_url="$(run_workflow WaSubCheck000001 | grep -oE '"callback_url": *"[^"]*"' | sed -E 's/.*"([^"]*)"$/\1/' || true)"
if [[ "$now_url" == "$expected" ]]; then
  echo "OK  Meta webhook -> $now_url"
  echo "Send a WhatsApp message to the test number and check app.wa_inbound_messages."
else
  die "Meta callback is '$now_url', expected '$expected'. n8n does not retry a refused registration: run n8n-import, then tunnel-refresh again"
fi
