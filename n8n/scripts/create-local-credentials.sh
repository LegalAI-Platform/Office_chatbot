#!/bin/sh
# Creates the n8n credentials whose values already live in .env (no Meta secrets):
#   office-postgres-app  (Postgres, user office_app)
#   hazem-api            (Header Auth X-Hazem-Token = HAZEM_API_TOKEN, n8n -> .NET API)
#   hazem-internal-send  (Header Auth X-Hazem-Token = N8N_INTERNAL_SEND_TOKEN, checked by WA-INTERNAL-SEND)
#   office-s3            (S3 on Garage with the n8n key S3_N8N_*: office-media and office-sensitive only)
# Runs inside the n8n container; n8n-import passes the values with `docker compose exec -e NAME`
# (names only, so the values never appear on a command line).
# The temporary file never leaves the container and is deleted right after the import.
# Idempotent: an existing credential with the same id is replaced.
set -eu

: "${OFFICE_APP_PASSWORD:?missing}"
: "${HAZEM_API_TOKEN:?missing}"
: "${N8N_INTERNAL_SEND_TOKEN:?missing}"
: "${S3_N8N_ACCESS_KEY_ID:?missing}"
: "${S3_N8N_SECRET_ACCESS_KEY:?missing}"
: "${S3_REGION:?missing}"

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
  },
  {
    "id": "HazemApiCred0001",
    "name": "hazem-api",
    "type": "httpHeaderAuth",
    "data": { "name": "X-Hazem-Token", "value": "${HAZEM_API_TOKEN}" }
  },
  {
    "id": "HazemIntSend0001",
    "name": "hazem-internal-send",
    "type": "httpHeaderAuth",
    "data": { "name": "X-Hazem-Token", "value": "${N8N_INTERNAL_SEND_TOKEN}" }
  },
  {
    "id": "OfficeS3Cred0001",
    "name": "office-s3",
    "type": "s3",
    "data": {
      "endpoint": "http://garage:3900",
      "region": "${S3_REGION}",
      "accessKeyId": "${S3_N8N_ACCESS_KEY_ID}",
      "secretAccessKey": "${S3_N8N_SECRET_ACCESS_KEY}",
      "forcePathStyle": true,
      "ignoreSSLIssues": false
    }
  }
]
EOF

n8n import:credentials --input="$tmp" >/dev/null
echo "credentials office-postgres-app, hazem-api, hazem-internal-send, office-s3 are ready"
