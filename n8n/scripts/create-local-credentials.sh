#!/bin/sh
# Creates the n8n credentials whose values already live in .env (no Meta secrets):
#   office-postgres-app  (Postgres, user office_app)
# Runs inside the n8n container; OFFICE_APP_PASSWORD is passed in with `docker compose exec -e`.
# The temporary file never leaves the container and is deleted right after the import.
# Idempotent: an existing credential with the same id is replaced.
set -eu

: "${OFFICE_APP_PASSWORD:?missing}"

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

cat > "$tmp" <<EOF
[
  {
    "id": "OfficePgApp00001",
    "name": "office-postgres-app",
    "type": "postgres",
    "data": {
      "host": "postgres",
      "port": 5432,
      "database": "office",
      "user": "office_app",
      "password": "${OFFICE_APP_PASSWORD}",
      "ssl": "disable",
      "allowUnauthorizedCerts": false
    }
  }
]
EOF

n8n import:credentials --input="$tmp" >/dev/null
echo "credential office-postgres-app is ready"
