# n8n credentials (documentation only — no values)

Credentials are created by hand in the n8n UI (Settings → Credentials) and stored encrypted
with `N8N_ENCRYPTION_KEY`. Never put their values in workflow JSON, files or git.
Exported workflows in `n8n/workflows/` reference credentials by name only.

| Credential name | n8n type | Connection details | Task |
|---|---|---|---|
| `office-postgres-app` | Postgres | host `postgres`, port `5432`, database `office`, user `office_app`, password = `OFFICE_APP_PASSWORD`, SSL off (internal network) | 02 |
| `office-s3` | S3 | endpoint `http://garage:3900`, region `garage`, force path style on, access key = `S3_N8N_ACCESS_KEY_ID`, secret = `S3_N8N_SECRET_ACCESS_KEY` | 03 |
| `office-redis` | Redis | host `redis`, port `6379`, password = `REDIS_PASSWORD`, database `0` | 03 |
| `meta-whatsapp` | HTTP Header Auth (`Authorization: Bearer <token>`) or WhatsApp API | permanent system-user token | 02 |
| `meta-app-secret` | (used by the signature-check step) | Meta App Secret | 02 |
| `openai` | OpenAI | API key; model from `OPENAI_MODEL` | 04 |
| `google-calendar` | Google Calendar OAuth2 / service account | lawyer's calendar | 08 |
| `ops-telegram` | Telegram | bot token; chat id from `OPS_TELEGRAM_CHAT_ID` | monitoring |

Rules:
- `office-postgres-app` is the only database credential workflows use. `office_migrator`
  and the superuser are never configured in n8n.
- The S3 credential is the n8n key. It cannot touch `office-backups`.
- Files from `office-sensitive` must never be sent to the `openai` credential (T-09).
