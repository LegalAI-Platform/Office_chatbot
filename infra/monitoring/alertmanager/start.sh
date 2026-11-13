#!/bin/sh
# Alertmanager's start (v1.107, docs/runbooks/monitoring.md). Alertmanager reads no environment, so this fills the alert
# e-mails and the mail server from .env into its config, on /tmp (memory), then runs it. The SMTP key (the panel's own,
# SMTP_PASSWORD) goes into a file of its own on /tmp that smtp_auth_password_file reads: never into the config, never printed.
set -eu
fail() { echo "alertmanager: $*" >&2; exit 1; }

email='[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
[ -n "${ALERT_EMAIL_TO:-}" ] || fail "ALERT_EMAIL_TO is empty in .env (docs/runbooks/monitoring.md, «Turning it on»)"
printf '%s' "$ALERT_EMAIL_TO" | grep -Eq "^$email( *, *$email)*\$" \
  || fail "ALERT_EMAIL_TO must be e-mail addresses separated by commas"
printf '%s' "${SMTP_HOST:-}" | grep -Eq '^[A-Za-z0-9.-]+$' || fail "SMTP_HOST is empty or not a host name (the panel's mail)"
printf '%s' "${SMTP_PORT:-}" | grep -Eq '^[0-9]{1,5}$' || fail "SMTP_PORT must be a number"
printf '%s' "${SMTP_FROM:-}" | grep -Eq "^$email\$" || fail "SMTP_FROM must be one e-mail address (the panel's sender)"
[ -z "${SMTP_USERNAME:-}" ] || printf '%s' "$SMTP_USERNAME" | grep -Eq '^[A-Za-z0-9._@+-]+$' \
  || fail "SMTP_USERNAME has characters it cannot have"
# STARTTLS like the panel's own mail (SMTP_SSL=true, port 587); only a test mail catcher has SMTP_SSL=false.
tls=true
[ "${SMTP_SSL:-true}" = "false" ] && tls=false

umask 077
printf '%s' "${SMTP_PASSWORD:-}" > /tmp/smtp_password
sed -e "s|__ALERT_EMAIL_TO__|$ALERT_EMAIL_TO|g" \
    -e "s|__SMTP_HOST__|$SMTP_HOST|g" \
    -e "s|__SMTP_PORT__|$SMTP_PORT|g" \
    -e "s|__SMTP_FROM__|$SMTP_FROM|g" \
    -e "s|__SMTP_USERNAME__|${SMTP_USERNAME:-}|g" \
    -e "s|__SMTP_REQUIRE_TLS__|$tls|g" \
    /etc/alertmanager/alertmanager.yml > /tmp/alertmanager.yml

# One Alertmanager, so no cluster port; the links in its e-mails are its own page through the SSH tunnel.
exec /bin/alertmanager \
  --config.file=/tmp/alertmanager.yml \
  --storage.path=/alertmanager \
  --web.listen-address=:9093 \
  --web.external-url=http://127.0.0.1:9093/ \
  --cluster.listen-address=
