#!/bin/bash
# Starts the core services plus a Cloudflare tunnel that exposes n8n ONLY.
# CLOUDFLARE_TUNNEL_TOKEN set -> Named Tunnel (stable hostname)
# CLOUDFLARE_TUNNEL_TOKEN empty -> Quick Tunnel (new random URL every start)
source "$(dirname "$0")/_common.sh"
check_no_placeholders

if [[ -n "$(env_value CLOUDFLARE_TUNNEL_TOKEN)" ]]; then
  docker compose --profile tunnel-named up -d --wait
  echo "Named Tunnel is up. Its public hostname is the one configured in the Cloudflare dashboard."
  exit 0
fi

docker compose --profile tunnel up -d --wait
url=""
for _ in $(seq 1 30); do
  url="$(docker compose --profile tunnel logs tunnel 2>/dev/null | grep -oE 'https://[a-z0-9-]+\.trycloudflare\.com' | tail -1 || true)"
  [[ -n "$url" ]] && break
  sleep 2
done
[[ -n "$url" ]] || die "could not read the tunnel URL. Check: docker compose --profile tunnel logs tunnel"

current="$(env_value WEBHOOK_URL)"
echo
echo "Tunnel URL: $url"
if [[ "$current" != "$url/" ]]; then
  echo
  echo "WEBHOOK_URL in .env is '$current'. To make n8n generate HTTPS webhook URLs:"
  echo "  1. Set in .env:  WEBHOOK_URL=$url/   and   N8N_SECURE_COOKIE=true"
  echo "  2. Restart n8n:  docker compose up -d n8n"
  echo "This Quick Tunnel URL changes every time the tunnel restarts."
fi
