#!/bin/bash
# Starts (or reuses) the Cloudflare Quick Tunnel and registers its URL as the Meta webhook.
# Run it at the start of every test session and after any restart of Docker or the machine:
# the Quick Tunnel URL changes every time the tunnel container restarts (DECISIONS v1.1, D-5).
source "$(dirname "$0")/_common.sh"
check_no_placeholders
[[ "$(env_value WhatsApp__Enabled)" == "true" ]] || die "WhatsApp__Enabled is not true in .env. Finish docs/meta-setup.md first."

state=".local/tunnel-url"
mkdir -p .local

docker compose --profile tunnel up -d --wait api tunnel >/dev/null
url=""
for _ in $(seq 1 30); do
  url="$(docker compose --profile tunnel logs tunnel 2>/dev/null | grep -oE 'https://[a-z0-9-]+\.trycloudflare\.com' | tail -1 || true)"
  [[ -n "$url" ]] && break
  sleep 2
done
[[ -n "$url" ]] || die "could not read the tunnel URL. Check: docker compose --profile tunnel logs tunnel"
callback="$url/webhooks/whatsapp"

# Quick Tunnel DNS can take a few seconds; Meta verifies the URL immediately.
for _ in $(seq 1 20); do
  [[ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$url/health/live")" == "200" ]] && break
  sleep 3
done

current="$(docker compose run --rm cli wa-check 2>/dev/null | json_string callback_url || true)"
if [[ "$current" == "$callback" ]]; then
  echo "Webhook already points to $callback"
  echo "$url" > "$state"
  exit 0
fi

echo "Registering webhook: $callback"
docker compose run --rm cli wa-subscribe-waba >/dev/null || echo "WARN: could not confirm the WABA subscription (see: cli.sh wa-check)"
docker compose run --rm cli wa-subscribe --callback "$callback" || die "Meta rejected the webhook. See docs/runbooks/whatsapp.md"

registered="$(docker compose run --rm cli wa-check 2>/dev/null | json_string callback_url || true)"
[[ "$registered" == "$callback" ]] || die "Meta still reports '$registered' instead of '$callback'"
echo "$url" > "$state"
echo "OK  Meta webhook -> $callback  ($(date '+%Y-%m-%d %H:%M'))"
