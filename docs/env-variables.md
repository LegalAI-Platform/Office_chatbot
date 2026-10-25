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
| `HAZEM_OPENAI_API_KEY` | api (`Hazem__OpenAiApiKey`): intent classification | **yes** | The .NET key from OpenAI, separate from n8n's voice key (T-08). **May be empty**: classification off, Hazem answers text as unclear, healthcheck WARN. Not made by `gen-secrets`. Never printed or logged | 04 |
| `OPENAI_MODEL` | api (`Hazem__OpenAiModel`) | no | empty = `gpt-4.1-mini`, a starting point; the owner picks the final model with `classify-eval` ([ai/classification.md](ai/classification.md)) | 04 |
| `PANEL_SECRET_KEY` | api (`Panel__SecretKey`): hashes the e-mail codes and recovery codes, encrypts the authenticator secrets | **yes** | 64 hex chars (`gen-secrets`). Empty = sign-up and 2FA "not available", sign-in still works. Changing it means everyone sets 2FA up again | v1.9 |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_SSL` | api (`Mail__Host`, `Mail__Port`, `Mail__Ssl`): the no-reply sender | no | the owner's provider; port `587` with STARTTLS (`SMTP_SSL=true`); 465 is not supported. Empty host = sign-up "not available" | v1.9 |
| `SMTP_USERNAME`, `SMTP_PASSWORD` | api (`Mail__Username`, `Mail__Password`) | **yes** (password) | from the provider. Never printed or logged | v1.9 |
| `SMTP_FROM`, `SMTP_FROM_NAME` | api (`Mail__From`, `Mail__FromName`) | no | e.g. `no-reply@<domain>`; empty name = the office's name | v1.9 |
| `S3_API_ACCESS_KEY_ID`, `S3_API_SECRET_ACCESS_KEY` | api (`S3__*`); storage-init | **yes** | `GK` + 24 hex / 64 hex (`gen-secrets`): read-only on `office-media` and `office-sensitive`, nothing on backups. The panel opens client files with it | 07c |
| `PANEL_MANAGERS_NEED_2FA` | api (`Panel__ManagersNeedTwoFactor`) | no | empty / `false` = the authenticator is optional for everyone (v1.16, the owner's decision); `true` = a manager must turn it on before anything else works (v1.9) | 12 |
| `PANEL_URL` | api (`Panel__PublicUrl`) | no | empty = `http://127.0.0.1:8080`: the link in alert e-mails | 07c |
| `CHATWOOT_SECRET_KEY_BASE`, `CHATWOOT_DB_PASSWORD`, `CHATWOOT_REDIS_PASSWORD` | chatwoot, chatwoot-sidekiq, chatwoot-db, chatwoot-redis | **yes** | from `gen-secrets` (TASK 07b) | 07b |
| `CHATWOOT_BOT_TOKEN` | api (`Chatwoot__BotToken`); `chatwoot-setup` gives it to «حازم (النظام)» | **yes** | from `gen-secrets`. Empty = the Chatwoot link is off | 07b |
| `CHATWOOT_PLATFORM_TOKEN` | api (`Chatwoot__PlatformToken`); `chatwoot-setup` gives it to the Platform App | **yes** | from `gen-secrets`: makes and disables the staff's Chatwoot users | 07b |
| `CHATWOOT_WEBHOOK_TOKEN` | api (`Chatwoot__WebhookSecret`); the inbox's webhook secret | **yes** | from `gen-secrets`: Chatwoot signs every webhook with it | 07b |
| `CHATWOOT_URL` | api (`Chatwoot__PublicUrl`), chatwoot (`FRONTEND_URL`) | no | empty = `http://127.0.0.1:3000`: where people open Chatwoot | 07b |
| `OPENAI_BASE_URL` | api (`Hazem__OpenAiBaseUrl`) | no | empty = `https://api.openai.com/v1` (healthcheck checks it). Tests never change it: `hazem-e2e` uses `hazem.settings ai.stub_*` | 04 |
| `GOOGLE_SERVICE_ACCOUNT_JSON` | api (`Hazem__GoogleServiceAccountJson`): the lawyer's Google Calendar (T-14, P2) | **yes** | the service account's key file as one line of base64 ([runbooks/booking.md](runbooks/booking.md)). Not made by `gen-secrets`; the owner only. **May be empty**: Hazem offers no booking times, healthcheck WARN. Never printed or logged | 08 |

## Non-secret API settings: `hazem.settings`

| Key | Value |
|---|---|
| `responder` | `hazem` (TASK 04). `echo` only inside `hazem-e2e` for the TASK 02/03 loop; the healthcheck fails otherwise |
| `hazem.recipients` | comma-separated `wa_id` list Hazem answers (test numbers) |
| `hazem.open_to_all` | `false`. `true` = Hazem answers **every** number — the owner's decision only (CLAUDE.md); healthcheck WARN |
| `ai.min_confidence` | `0.6`: below it a turn is treated as unclear |
| `ai.stub_enabled`, `ai.stub_base_url` | `false` / empty. Only inside `hazem-e2e`, and honoured only when `ENVIRONMENT=test` |
| `echo.enabled`, `echo.recipients` | the TASK 02/03 test echo (`false` / empty) |
| `handoff.enabled` | `false` (TASK 05a). `true` = «موظف» hands the conversation to the staff and Hazem goes silent (B-12). Stays `false` until staff can answer from Chatwoot (TASK 07b, DECISIONS v1.8): nobody would answer the client. The healthcheck fails while it is `true` |
| `source.link_codes` | comma-separated codes put in wa.me links (e.g. `FB-OCT`); a first message containing one is recorded as source `link` (B-13) |
| `booking.enabled` | `false` (TASK 08). `true` = Hazem offers free times and books; changed in the panel (إعدادات الحجز). Works only when the booking's texts are approved too |
| `booking.buffer_minutes`, `booking.min_notice_hours`, `booking.horizon_days`, `booking.max_per_day`, `booking.slot_step_minutes`, `booking.offer_count` | `10`, `2`, `14`, empty (no limit), `15`, `4` (B-24, P5): changed in the panel |
| `booking.calendar_id` | the lawyer's Google Calendar (his e-mail), shared with the service account; the panel |
| `booking.google_base_url` | empty. Only inside `hazem-e2e` (the Google Calendar stub), honoured only when `ENVIRONMENT=test`; the healthcheck fails otherwise |
| `booking.conflict_auto_message` | `false` (P3): a calendar event over a booking makes a task only |
| `office.instapay`, `office.wallet_number`, `office.wallet_provider`, `office.address`, `office.map_url` | P1: written by the manager or the lawyer in the panel (بيانات المكتب); the booking's confirmation uses the first three |

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
