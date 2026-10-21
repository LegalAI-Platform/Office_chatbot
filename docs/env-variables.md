# Environment variables

All values live in `.env` (never committed). `.env.example` has every variable with a
placeholder. `scripts/*/gen-secrets` prints fresh values for every secret.
The `preflight` job refuses to start the stack while a required value is empty, still
contains `CHANGE_ME`, or has the wrong format.

| Variable | Used by | Secret | Example / format | Task |
|---|---|---|---|---|
| `ENVIRONMENT` | preflight, reset | no | `test` (or `staging`) | 01 |
| `COMPOSE_PROJECT_NAME` | docker compose | no | `office` | 01 |
| `POSTGRES_SUPERUSER` | postgres, tools | no | `postgres_admin` | 01 |
| `POSTGRES_SUPERUSER_PASSWORD` | postgres, tools | **yes** | 48 hex chars | 01 |
| `N8N_DB_PASSWORD` | postgres init, n8n | **yes** | 48 hex chars, URL-safe | 01 |
| `OFFICE_MIGRATOR_PASSWORD` | postgres init, migrate | **yes** | 48 hex chars, URL-safe | 01 |
| `OFFICE_APP_PASSWORD` | postgres init, tools; n8n credential `office-postgres-app` (created by `create-local-credentials.sh`) | **yes** | 48 hex chars, URL-safe | 01 |
| `OFFICE_READONLY_PASSWORD` | postgres init, tools; the panel's read views later | **yes** | 48 hex chars, URL-safe | 01 |
| `OFFICE_API_PASSWORD` | postgres init, api (role `office_api`, schema `hazem` only), tools | **yes** | 48 hex chars, URL-safe | v1.3 |
| `REDIS_PASSWORD` | redis, tools; n8n credential later | **yes** | 48 hex chars | 01 |
| `GARAGE_RPC_SECRET` | garage | **yes** | exactly 64 lowercase hex | 01 |
| `GARAGE_ADMIN_TOKEN` | garage, storage-init, healthcheck | **yes** | 64 hex chars | 01 |
| `S3_REGION` | tools; n8n credential `office-s3` (created by `n8n-import`) | no | `garage` | 01 |
| `S3_N8N_ACCESS_KEY_ID` | storage-init; n8n credential `office-s3` (created by `n8n-import`) | no (identifier) | `GK` + 24 hex | 01 |
| `S3_N8N_SECRET_ACCESS_KEY` | storage-init; n8n credential `office-s3` (created by `n8n-import`) | **yes** | 64 hex | 01 |
| `S3_BACKUP_ACCESS_KEY_ID` | storage-init, backup, restore | no (identifier) | `GK` + 24 hex | 01 |
| `S3_BACKUP_SECRET_ACCESS_KEY` | storage-init, backup, restore | **yes** | 64 hex | 01 |
| `N8N_ENCRYPTION_KEY` | n8n | **yes — critical** | 64 hex. Losing or changing it makes stored credentials unreadable | 01 |
| `N8N_HOST` | n8n | no | `127.0.0.1` (or the tunnel hostname) | 01 |
| `N8N_PROTOCOL` | n8n | no | `http` locally, `https` behind the tunnel | 01 |
| `WEBHOOK_URL` | n8n (passed as `N8N_WEBHOOK_URL`, the n8n 2.x name) | no | `http://127.0.0.1:5678/` or `https://<tunnel>/` (trailing slash) | 01 / 02 |
| `N8N_PROXY_HOPS` | n8n | no | `1` (cloudflared) | 01 |
| `N8N_SECURE_COOKIE` | n8n | no | `false` for local http, `true` behind HTTPS | 01 |
| `EXECUTIONS_DATA_MAX_AGE` | n8n | no | hours; `336` = 14 days | 01 |
| `HAZEM_API_TOKEN` | api; n8n credential `hazem-api` (n8n → API, header `X-Hazem-Token`) | **yes** | 64 hex chars; different from the next one | v1.3 |
| `N8N_INTERNAL_SEND_TOKEN` | api; n8n credential `hazem-internal-send` (API → n8n internal send webhook) | **yes** | 64 hex chars | v1.3 |
| `CLOUDFLARE_TUNNEL_TOKEN` | tunnel-named | **yes** | empty = Quick Tunnel | 01 / 02 |
| `OPENAI_MODEL` | n8n workflows | no | empty until sample tests (T-08) | 04 |

## Non-secret WhatsApp settings: `app.settings`, not `.env`

Meta identifiers live in the database table `app.settings` and are read by the `CFG-GET`
workflow (n8n Code nodes cannot read environment variables, by design):
`whatsapp.app_id`, `whatsapp.waba_id`, `whatsapp.phone_number_id`,
`whatsapp.graph_api_version` (`v26.0`), `whatsapp.test_recipients`, `whatsapp.echo_test_enabled`,
`ops.telegram_chat_id`. See [whatsapp-integration.md](whatsapp-integration.md#settings-appsettings-non-secret).

Variables set directly in `docker-compose.yml` (not in `.env`): `TZ` / `GENERIC_TIMEZONE`
(`Africa/Cairo`), `DB_TYPE=postgresdb`, `N8N_DIAGNOSTICS_ENABLED=false`,
`N8N_VERSION_NOTIFICATIONS_ENABLED=false`, `N8N_PERSONALIZATION_ENABLED=false`,
`N8N_BLOCK_ENV_ACCESS_IN_NODE=true`, `EXECUTIONS_DATA_PRUNE=true`,
`APPSMITH_DISABLE_TELEMETRY=true`.

## Secrets that never go in `.env`

They are entered as **n8n Credentials** (encrypted with `N8N_ENCRYPTION_KEY`), never in
`.env`, files, workflow JSON or logs:

| Secret | Task |
|---|---|
| WhatsApp permanent access token (`meta-whatsapp-token`) | 02 |
| Meta App Secret (`meta-whatsapp-trigger`, `meta-app-token`) | 02 |
| OpenAI API key for voice transcription (`openai-transcription`) | 03 |
| OpenAI API key for intent classification (.NET, T-08) | 04 |
| Google Calendar OAuth / service account | 08 |
| Telegram bot token | monitoring |
