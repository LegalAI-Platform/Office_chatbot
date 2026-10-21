# Office WhatsApp System (Hazem) — Test/Staging infrastructure

WhatsApp assistant ("Hazem") for the law office of Ustaz Saad Fathy Saad.
**n8n** is the WhatsApp layer (the only part that talks to Meta WhatsApp Cloud API), the
**.NET API** (ASP.NET Core, `backend/`) is the brain, and PostgreSQL holds the data.

Done so far: **TASK 01** (infrastructure), **TASK 02** (Meta WhatsApp Cloud API in n8n; live
tests with Meta pending the project owner's Meta setup), the **n8n ↔ .NET hand-off**
(DECISIONS v1.3) and **TASK 03** (messages grouped into turns after 8 quiet seconds, media
downloaded to S3, voice notes transcribed). The API only answers with a test echo until TASK 04.

- All decisions: [docs/decisions/DECISIONS-v1.3.md](docs/decisions/DECISIONS-v1.3.md)
- n8n ↔ .NET API contract: [docs/hazem-internal-api.md](docs/hazem-internal-api.md)
- Backend: [backend/README.md](backend/README.md)
- Architecture and permissions: [docs/infrastructure.md](docs/infrastructure.md)
- WhatsApp workflows, error codes, settings: [docs/whatsapp-integration.md](docs/whatsapp-integration.md)
- Operations runbook: [docs/runbooks/whatsapp.md](docs/runbooks/whatsapp.md)
- Meta setup (project owner): [docs/meta-setup.md](docs/meta-setup.md)
- Environment variables: [docs/env-variables.md](docs/env-variables.md)

TASK 02 commands: `n8n-import` / `n8n-export` (workflows and the credentials from `.env`),
`wa-tests` (database rules and the workflow harness, no Meta needed), `tunnel-refresh` /
`tunnel-down` (Quick Tunnel + Meta webhook).
v1.3 / TASK 03 commands: `backend-test` (.NET unit and integration tests), `hazem-e2e` (the
whole loop n8n → API → n8n with grouping, media and voice, against a test stub instead of Meta
and OpenAI).

## Services

| Service | Local URL / port | Purpose | Starts with |
|---|---|---|---|
| n8n 2.43.8 | http://127.0.0.1:5678 | WhatsApp layer: Meta in and out (Regular mode, PostgreSQL backend) | core |
| .NET API (ASP.NET Core 10) | none (`http://api:8080` inside Docker only) | The brain: turns, decisions, later the panel API | core |
| PostgreSQL 16.15 | 127.0.0.1:5432 | Databases `n8n` and `office` | core |
| Redis 7.4.11 | 127.0.0.1:6379 | Debounce, dedup, locks, rate limits (later tasks) | core |
| Garage v2.4.1 (S3) | http://127.0.0.1:3900 | Object storage: `office-media`, `office-sensitive`, `office-backups` | core |
| Appsmith CE v2.4.3 | http://127.0.0.1:8080 | Unused: the panel will be React on the API (TASK 07) | `panel` profile |
| meta-stub (.NET, test only) | none | Stand-in for Meta media and OpenAI transcription during `hazem-e2e` | `tests` profile, started by `hazem-e2e` only |
| cloudflared 2026.9.3 | public https URL | Exposes **n8n only** for the Meta webhook | `tunnel` / `tunnel-named` profile |

Every port is bound to `127.0.0.1`. Nothing is reachable from the network except n8n,
and only while a tunnel profile is running.

## Requirements

- **Windows 10/11:** Docker Desktop with the WSL2 backend.
- **Linux (VPS later):** Docker Engine with the Compose plugin.
- Nothing else is installed on the machine; every tool runs in a container.

Windows PowerShell may block local scripts. Run them like this:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\windows\up.ps1
```

Every script below exists in two versions: `scripts\windows\<name>.ps1` and
`scripts/linux/<name>.sh`. Examples show the Windows form.

## First run (from zero)

1. **Create `.env`**
   ```powershell
   Copy-Item .env.example .env
   ```
2. **Generate secrets** and paste the printed lines over the `CHANGE_ME` values in `.env`:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\windows\gen-secrets.ps1
   ```
   Save a copy of `N8N_ENCRYPTION_KEY` somewhere safe outside this machine.
3. **Start the core services** (refuses to start while `.env` has any `CHANGE_ME`):
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\windows\up.ps1
   ```
4. **Apply migrations:**
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\windows\migrate.ps1 up
   ```
   Then import the n8n workflows (also creates the credentials that come from `.env`):
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\windows\n8n-import.ps1
   ```
5. **Check everything:**
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\windows\healthcheck.ps1
   ```
   Every line must say `PASS`.
6. **Create the n8n owner account by hand:** open http://127.0.0.1:5678 and fill the
   setup form. There is no default password. Do this **before** you start a tunnel,
   otherwise anyone with the tunnel URL could claim the owner account.

## Everyday commands

| Script | What it does |
|---|---|
| `up` | Start core services and wait until healthy |
| `up-panel` | Core + Appsmith |
| `up-tunnel` | Core + Cloudflare tunnel, prints the public URL |
| `down` | Stop everything, keep all data |
| `status` | Containers, health, ports |
| `logs <service> [lines]` | Follow one service's logs |
| `healthcheck` | Full check (containers, ports, DB roles, n8n, Redis, buckets, keys, migrations) |
| `migrate up\|down\|status` | Run migrations on `office` as `office_migrator` |
| `n8n-import` | Credentials from `.env` + workflows into n8n; restarts n8n when a schedule or webhook was published |
| `wa-tests` | WhatsApp database rules + n8n harness (no Meta) |
| `backend-test [-Docker]` | .NET tests; uses a throwaway database (needs `up`) |
| `hazem-e2e` | Whole loop n8n ↔ API without Meta or OpenAI (stub, dry run), then restores every setting and removes its temporary credentials |
| `backup` | Dump `office` and `n8n` to `office-backups` |
| `restore ...` | Restore a dump into a new database and verify it |
| `reset` | **Deletes all data.** Only with `ENVIRONMENT=test` and after typing `RESET` |
| `gen-secrets` | Print fresh random secrets (never edits `.env`) |

## Tunnel (HTTPS for the Meta webhook)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\windows\up-tunnel.ps1
```

- **Quick Tunnel** (default, `CLOUDFLARE_TUNNEL_TOKEN` empty): no account needed, but the
  `https://*.trycloudflare.com` URL **changes every time** the tunnel restarts. After each
  start, set `WEBHOOK_URL=<url>/` and `N8N_SECURE_COOKIE=true` in `.env`, then run
  `docker compose up -d n8n`. The URL must also be updated in Meta (TASK 02).
- **Named Tunnel** (recommended before connecting Meta): create a tunnel in the Cloudflare
  dashboard (free account), add a public hostname pointing to `http://n8n:5678`, put the
  token in `CLOUDFLARE_TUNNEL_TOKEN`, and set `WEBHOOK_URL=https://<your-hostname>/`.
  The URL then stays the same.

The tunnel forwards to n8n only. PostgreSQL, Redis, Garage and Appsmith are never exposed.

## Backup and restore

```powershell
# Back up office + n8n (Cairo-time timestamps, kept 30 days)
powershell -ExecutionPolicy Bypass -File scripts\windows\backup.ps1

# List backups
powershell -ExecutionPolicy Bypass -File scripts\windows\restore.ps1 list

# Restore the latest office dump into a NEW database and verify row counts
powershell -ExecutionPolicy Bypass -File scripts\windows\restore.ps1 office latest

# Same, but drop the restored copy after a successful verification (restore drill)
powershell -ExecutionPolicy Bypass -File scripts\windows\restore.ps1 n8n latest --drop-after-verify
```

`restore` never overwrites the live `office` or `n8n` databases. To replace a live database
with a restored copy, stop n8n, rename the databases by hand with `psql`, and start n8n again.

### Daily backup

**Windows (Task Scheduler), every day at 03:00.** Docker Desktop must be running.
Replace `<REPO>` with the full path of this folder:

```powershell
schtasks /Create /SC DAILY /ST 03:00 /TN "OfficeWhatsAppBackup" /TR "powershell -NoProfile -ExecutionPolicy Bypass -File \"<REPO>\scripts\windows\backup.ps1\""
```

**Linux (cron):**

```bash
0 3 * * * /path/to/repo/scripts/linux/backup.sh >> /var/log/office-backup.log 2>&1
```

Backups older than 30 days are deleted automatically by the bucket's lifecycle rule.

## Troubleshooting

| Problem | Fix |
|---|---|
| `port is already allocated` | Another program uses 5432/5678/6379/3900/8080. Stop it, or change the left side of the port mapping in `docker-compose.yml` (keep `127.0.0.1:`). |
| `preflight` exits with `PREFLIGHT FAIL` | `.env` still has a placeholder or a malformed value. The message names the variable. |
| Docker commands fail on Windows | Start Docker Desktop and make sure Settings → General → "Use the WSL 2 based engine" is on. |
| `bad interpreter` or `\r` errors in containers | A `.sh`/`.sql` file got Windows line endings. `.gitattributes` forces LF; re-checkout the file (`git checkout -- <file>`). |
| `Mismatching encryption keys` / n8n credentials unreadable | `N8N_ENCRYPTION_KEY` changed. Restore the original key. Without it, stored credentials cannot be recovered. |
| Git Bash rewrites paths like `/docker-entrypoint-initdb.d` | Prefix the command with `MSYS_NO_PATHCONV=1`. |
| n8n login loops on http://127.0.0.1 | Keep `N8N_SECURE_COOKIE=false` for plain-http local use; set `true` only behind HTTPS. |
