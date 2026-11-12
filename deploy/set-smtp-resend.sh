#!/bin/bash
# The panel's e-mails (the codes, the alerts) through Resend, from no-reply@hazem-office.tech (the owner, 2026-11-12).
# Run on the server as «office»; over SSH from the PC:
#   ssh -t office@<server> bash /opt/office/deploy/set-smtp-resend.sh [from-address]
# It asks for the Resend key and never shows, prints or logs it. Make the key at resend.com → API Keys → Create API Key,
# with «Sending access» and the domain only. The script sets the SMTP_* values in .env and restarts the api.
# To change the key, make a new one, run this, then delete the old one in Resend. The domain's DNS records for Resend
# are in docs/runbooks/server.md §9.
set -euo pipefail
cd "$(dirname "$0")/.."

die() { echo "ERROR: $*" >&2; exit 1; }
[[ -f .env ]] || die ".env not found in $PWD"
[[ "$(id -un)" != "root" ]] || die "run as «office», not root"
from="${1:-no-reply@hazem-office.tech}"
[[ "$from" =~ ^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$ ]] || die "not an e-mail address: $from"

read -rsp "Paste the Resend API key (it starts with re_) and press Enter: " key; echo
if [[ ! "$key" =~ ^re_[A-Za-z0-9_]{20,}$ ]]; then unset key; die "that is not a Resend key (re_...); nothing changed"; fi

set_env() {
  if grep -qE "^$1=" .env; then sed -i "s#^$1=.*#$1=$2#" .env; else echo "$1=$2" >> .env; fi
}
set_env SMTP_HOST smtp.resend.com
set_env SMTP_PORT 587
set_env SMTP_SSL true
set_env SMTP_USERNAME resend
set_env SMTP_FROM "$from"
set_env SMTP_PASSWORD "$key"
unset key

docker compose up -d api
echo "Done: the panel's e-mails go from $from (the name: «$(grep -E '^SMTP_FROM_NAME=' .env | cut -d= -f2-)»)."
