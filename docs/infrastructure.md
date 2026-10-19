# Infrastructure — Test/Staging (DECISIONS v1.1)

Reference: [DECISIONS-v1.1.md](decisions/DECISIONS-v1.1.md) — T-01 to T-06, T-09, T-12 to T-23.
Component diagram and request flow: [architecture.md](architecture.md).

Startup order: `preflight` → `postgres`, `redis`, `garage` → `storage-init` → `api`.
`preflight` and `storage-init` are one-shot jobs; everything else restarts `unless-stopped`.
Migrations are applied by `cli migrate`, never by the API.

## PostgreSQL roles and privileges

| Role | Can do | Cannot do | Used by |
|---|---|---|---|
| `postgres_admin` (superuser) | everything | — | init script, backup, restore, healthcheck, test DB setup |
| `office_migrator` (owner of `office`) | DDL; owns schemas `app` and `hangfire` | — | `cli migrate` only |
| `office_app` | `SELECT, INSERT, UPDATE, DELETE` in `app` and `hangfire`; sequences | `CREATE`/`ALTER` anything | the API (and CLI commands other than migrate) |
| `office_readonly` | `SELECT` in `app` | any write | reports |

- `CONNECT` is revoked from `PUBLIC` on `office` and `postgres`; `CREATE` on `public` is revoked.
- The first EF migration sets default privileges in schema `app`, so every future table is granted
  automatically (also on new test databases).
- Server timezone `Africa/Cairo`; application roles use `search_path = app, public`.
- The n8n database and its role were removed in TASK 02 (DECISIONS v1.1).

## Object storage

T-06 asked for MinIO in test. **MinIO no longer publishes container images**, so the test
environment uses **Garage** (S3-compatible, per-key bucket permissions, lifecycle expiry).
Production stays on **Cloudflare R2**.

| Bucket | Content | Access |
|---|---|---|
| `office-media` | voice notes, regular images and files | app key: read + write |
| `office-sensitive` | ID cards, Urfi documents, payment receipts | app key: read + write |
| `office-backups` | PostgreSQL dumps, expire after 30 days | backup key: read + write + owner |

| Access key | `office-media` | `office-sensitive` | `office-backups` |
|---|---|---|---|
| app (`S3_APP_ACCESS_KEY_ID`) — the backend from TASK 03 | read, write | read, write | none |
| backup (`S3_BACKUP_ACCESS_KEY_ID`) | none | none | read, write, owner |

- No bucket is public; anonymous requests are rejected. The Garage admin API is internal only.
- Garage has **no object versioning / object lock** (R2 neither); protection comes from separate
  buckets, least-privilege keys and daily database backups.

### Sensitive files and AI (T-09)

Anything in `office-sensitive` — ID cards, Urfi documents, receipts — **must never be sent to any
AI provider**. The backend sends the AI only the text it needs for intent classification (T-07).

## Network exposure

- Every published port is bound to `127.0.0.1`.
- Only the API can be reached from the internet, and only through cloudflared (`tunnel` /
  `tunnel-named` profiles), which forwards to `http://api:8080` and nothing else.
- The webhook rejects unsigned requests (401); see [whatsapp-integration.md](whatsapp-integration.md).

## Moving to production (documentation only)

| Test | Production |
|---|---|
| Docker Desktop on Windows (data on K:) | Dedicated VPS (decided after testing, T-01) |
| Garage | Cloudflare R2, separate buckets, scoped API tokens |
| Quick Tunnel (URL changes) | Real domain with HTTPS (T-13) |
| `ENVIRONMENT=test` | `ENVIRONMENT=production`, separate `.env` and Meta credentials |
| Meta test number | Office number via Coexistence or Migration (runbook) |
| Data Protection keys in the container | Persisted keys (before TASK 07 cookies) |
| Backups on Garage | Backups on R2 + regular restore drills |
