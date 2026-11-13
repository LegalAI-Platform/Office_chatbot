#!/bin/bash
# The masking's test (v1.107, docs/runbooks/monitoring.md): made-up log lines go through mask.alloy (the very module the
# stack's Alloy runs), and then:
#   - the nightly log-scan's own patterns (infra/tools/bin/log-scan) find nothing in what comes out;
#   - none of the made-up secrets is left, and each one shows masked; a number keeps its last 3 digits;
#   - lines with nothing secret come out unchanged;
#   - Alloy counted the lines the log-scan would have failed on (the LogsHavePhoneNumber / LogsHaveSecret alerts).
# The tokens and numbers are made here at random, so no token-shaped string is ever committed (gitleaks).
#   bash infra/monitoring/alloy/test/run.sh            (Docker; the Alloy image of docker-compose.yml)
set -euo pipefail
export MSYS_NO_PATHCONV=1 # Git Bash: container paths as they are
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../../.." && pwd)"
image="$(grep -oE 'grafana/alloy:v[0-9.]+' "$repo/docker-compose.yml" | head -1)"
name="hazem-alloy-mask-test"
work="$(mktemp -d "${TMPDIR:-/tmp}/hazem-mask.XXXXXX")"
cleanup() { docker rm -f "$name" >/dev/null 2>&1 || true; rm -rf "$work"; }
trap cleanup EXIT
host_path() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }

rand() { LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c "$1" || true; }
digits() { LC_ALL=C tr -dc '0-9' < /dev/urandom | head -c "$1" || true; }
operator() { local o=(0 1 2 5); echo "${o[$((RANDOM % 4))]}"; }

phone11="01$(operator)$(digits 8)"   # 010… 011… 012… 015…: 11 digits
phone12="201$(operator)$(digits 8)"  # the same as a WhatsApp id: 12 digits
phone_in_word="01$(operator)$(digits 8)"
bearer="$(rand 40)"
meta="EAA$(rand 60)"
openai="sk-$(rand 40)"
db_password="$(rand 24)"
url_password="$(rand 20)"
key_body="MIIE$(rand 60)"
pk="PRIVATE"" KEY" # in two parts, so this file never holds a key's first line itself (gitleaks' private-key rule)
key_begin="-----BEGIN ${pk}-----"
key_end="-----END ${pk}-----"
hazem_token="$(rand 48)"
resend="re_$(rand 30)"
jwt="eyJ$(rand 20).eyJ$(rand 30).$(rand 30)"
cookie="$(rand 32)"
not_a_phone="9$(digits 11)" # 12 digits that are not a number (an id): left alone, like the log-scan does

cat > "$work/lines.log" <<EOF
{"@t":"2026-11-13T10:00:00Z","@mt":"Turn from {Phone}","Phone":"${phone11}"}
wa_id=${phone12} status=delivered
to +${phone12} at once
client${phone_in_word}x asked again
order id ${not_a_phone} done
Authorization: Bearer ${bearer}
calling the graph api with token=${meta}
{"api_key":"${openai}","model":"x"}
Host=postgres;Database=office;Username=office_api;Password=${db_password}
DATABASE_URL=postgres://office_app:${url_password}@postgres:5432/office?sslmode=disable
{"key":"${key_begin}\n${key_body}\n${key_end}\n"}
X-Hazem-Token: ${hazem_token}
smtp key ${resend} refused
session ${jwt} expired
Cookie: hazem_auth=${cookie}; Path=/
Alerts: 3 task(s) overdue, 2 alert(s), 1 e-mail(s)
{"key":"whatsapp.dry_run","value":"false"}
EOF
lines="$(wc -l < "$work/lines.log" | tr -d ' ')"

docker rm -f "$name" >/dev/null 2>&1 || true
docker run -d --name "$name" -p 127.0.0.1:12399:12345 \
  -v "$(host_path "$repo/infra/monitoring/alloy"):/etc/alloy:ro" -v "$(host_path "$work"):/fixture:ro" \
  "$image" run --server.http.listen-addr=0.0.0.0:12345 --storage.path=/tmp/alloy --disable-reporting \
  /etc/alloy/test/config.alloy >/dev/null

out="$work/out.log"
for _ in $(seq 60); do
  docker logs "$name" > "$out" 2>&1 || true
  [[ "$(grep -c 'component_id=loki.echo.out' "$out" || true)" -ge "$lines" ]] && break
  sleep 1
done
metrics="$(curl -fsS http://127.0.0.1:12399/metrics || true)"

failed=0
fail() { echo "FAIL  $*"; failed=1; }
pass() { echo "PASS  $*"; }

echoed="$(grep -c 'component_id=loki.echo.out' "$out" || true)"
[[ "$echoed" -ge "$lines" ]] && pass "all $lines lines came out" || { fail "only $echoed of $lines lines came out"; tail -20 "$out"; }

# The nightly log-scan's own patterns (infra/tools/bin/log-scan), over everything Alloy printed.
if grep -qE 'EAA[A-Za-z0-9]{40,}|sk-[A-Za-z0-9_-]{24,}|[Bb]earer [A-Za-z0-9._~+/-]{24,}|BEGIN PRIVATE KEY' "$out"; then
  fail "the log-scan would find a token"; else pass "the log-scan finds no token"; fi
if grep -qE '(^|[^0-9])(20|0)1[0125][0-9]{8}([^0-9]|$)' "$out"; then
  fail "the log-scan would find a full phone number"; else pass "the log-scan finds no full phone number"; fi

for secret in "$bearer" "$meta" "$openai" "$db_password" "$url_password" "$key_body" "$hazem_token" "$resend" "$jwt" "$cookie" \
              "$phone11" "$phone12" "$phone_in_word"; do
  grep -qF -- "$secret" "$out" && fail "a made-up secret came out: ${secret:0:4}…"
done
[[ $failed == 0 ]] && pass "none of the 13 made-up secrets and numbers came out"

expect() { grep -qF -- "$2" "$out" && pass "$1" || fail "$1 (no \"$2\")"; }
expect "an 11-digit number keeps its last 3 digits" "********${phone11: -3}"
expect "a 12-digit WhatsApp id keeps its last 3 digits" "*********${phone12: -3}"
expect "a +20 number is masked too" "+*********${phone12: -3}"
expect "a longer run of digits (an id) is left alone" "order id ${not_a_phone} done"
expect "a bearer token" "Bearer ****"
expect "a database password" "Password=****"
expect "a password inside a URL" "office_app:****@postgres"
expect "a private key" "[private key masked]"
expect "a line with nothing secret is unchanged" "Alerts: 3 task(s) overdue, 2 alert(s), 1 e-mail(s)"
expect "a setting's name is not a secret" 'whatsapp.dry_run'

count() { grep -E "^loki_process_custom_$1\{" <<< "$metrics" | grep 'service="api"' | awk '{s += $NF} END {print s + 0}'; }
[[ "$(count log_lines_with_phone_total)" == "4" ]] && pass "4 lines counted as having a full number (the log-scan's pattern)" \
  || fail "counted $(count log_lines_with_phone_total) lines with a number, not 4"
[[ "$(count log_lines_with_secret_total)" == "4" ]] && pass "4 lines counted as having a token or a key (the log-scan's pattern)" \
  || fail "counted $(count log_lines_with_secret_total) lines with a token, not 4"

if [[ $failed != 0 ]]; then
  echo "The masking test FAILED (Alloy's output above is masked: it holds no secret)."
  exit 1
fi
echo "The masking test passed."
