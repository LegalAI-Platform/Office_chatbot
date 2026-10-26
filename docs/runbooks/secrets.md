# Secrets: where each one lives and how to replace it (TASK 13, DECISIONS v1.18)

Every secret is in `.env` (never in git, never in a chat). After changing one, restart what uses it
(`docker compose up -d <service>`) and run the healthcheck. **A secret that was pasted in a chat, an e-mail or a
screenshot must be replaced.** The nightly log scan checks that no token and no full phone number is ever logged.

| Secret | Used by | How to replace it |
|---|---|---|
| `HAZEM_OPENAI_API_KEY` | api (classification) | Make a new key at platform.openai.com → API keys, put it in `.env`, `docker compose up -d api`, then **delete the old key** on the site |
| `GOOGLE_SERVICE_ACCOUNT_JSON` | api (the lawyer's calendar) | Google Cloud → IAM → Service accounts → the account → Keys → Add key (JSON); put it in `.env`; `up -d api`; `calendar-check`; then delete the old key there |
| Meta's token (the n8n credential `meta-whatsapp-token`) | n8n | Meta Business → System users → generate a new token (the same permissions); in n8n: Credentials → the credential → paste; then revoke the old one |
| `SMTP_PASSWORD` (Gmail app password) | api (no-reply mail) | myaccount.google.com → App passwords → make a new one, put it in `.env`, `up -d api`, delete the old one |
| `POSTGRES_SUPERUSER_PASSWORD`, `OFFICE_*_PASSWORD`, `N8N_DB_PASSWORD` | postgres, the services | Change the role's password in Postgres (`ALTER ROLE … PASSWORD`) and in `.env` together, then restart the services that use it |
| `S3_*` keys | Garage | New values from `gen-secrets` in `.env`, then `docker compose run --rm tools storage-init` (it imports them) and restart api / n8n |
| `HAZEM_API_TOKEN`, `N8N_INTERNAL_SEND_TOKEN`, `CHATWOOT_*_TOKEN` | between our services | New values from `gen-secrets`, restart api, n8n and Chatwoot (`chatwoot-setup` again for the Chatwoot ones) |
| `PANEL_SECRET_KEY` | api | Changing it signs everyone out and **everyone sets 2FA up again**; only if it leaked |
| `N8N_ENCRYPTION_KEY` | n8n (its credentials) | **Never change it**: n8n could no longer read its credentials. Keep a copy outside this machine |
| `BACKUP_ENCRYPTION_KEY` | the backups | **Not casually**: a backup can only be read with the key it was made with. If it leaked: keep the old key apart (30 days), put a new one in `.env` (`gen-secrets`), run a backup at once. The new key starts its own copy of the files (`files/objects/<key id>/`) and never reuses or deletes the old key's; the old key's dumps and files expire after 30 days. To read one of them meanwhile: `docker compose run --rm -e BACKUP_ENCRYPTION_KEY=<the old key> tools restore …` |

## The n8n owner account

n8n's own login (the owner made at the first start) protects every workflow and credential. Since TASK 13 its page is
not on the internet (the edge), but turn on **two-factor authentication** for it anyway: n8n → Settings → Personal →
Two-factor authentication.
