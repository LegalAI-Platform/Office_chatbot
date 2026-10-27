# Office WhatsApp System (Hazem) — Test/Staging infrastructure

WhatsApp assistant ("Hazem") for the law office of Ustaz Saad Fathy Saad.
**n8n** is the WhatsApp layer (the only part that talks to Meta WhatsApp Cloud API), the
**.NET API** (ASP.NET Core, `backend/`) is the brain, and PostgreSQL holds the data.

Done so far: **TASK 01** (infrastructure), **TASK 02** (Meta WhatsApp Cloud API in n8n; live
tests with Meta pending the project owner's Meta setup), the **n8n ↔ .NET hand-off**
(DECISIONS v1.3) and **TASK 03** (messages grouped into turns after 8 quiet seconds, media
downloaded to S3, voice notes transcribed) and **TASK 04** (backend in layers with the state
machines S-01..S-06; Hazem classifies each turn with the AI — text only — and answers from
approved texts only; [docs/ai/classification.md](docs/ai/classification.md)), **TASK 05a** (who is
writing, topics, staff tasks) and **TASK 07a** (the panel at http://127.0.0.1:8080: sign-in with
roles, accounts, staff tasks; [docs/panel-api.md](docs/panel-api.md),
[docs/runbooks/panel.md](docs/runbooks/panel.md)) and **TASK 07b** (Chatwoot at http://127.0.0.1:3000:
the staff answer clients there; [docs/runbooks/chatwoot.md](docs/runbooks/chatwoot.md)) and **TASK 07c**
(alerts by e-mail and in the panel, the office's lists, Hazem's texts, client files, reports). Hazem answers only the numbers in
`hazem.settings hazem.recipients` until the owner opens it to everyone.

- All decisions: [docs/decisions/DECISIONS-v1.12.md](docs/decisions/DECISIONS-v1.12.md)
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
| n8n 2.44.7 | http://127.0.0.1:5678 | WhatsApp layer: Meta in and out (Regular mode, PostgreSQL backend) | core |
| .NET API (ASP.NET Core 10) | none (`http://api:8080` inside Docker only) | The brain: turns, decisions, the panel API (`/api/panel`) | core |
| Panel (React, nginx) | http://127.0.0.1:8080 | Staff tasks and accounts (TASK 07a); forwards `/api/panel` only | core |
| Chatwoot (Community Edition 4.18) | http://127.0.0.1:3000 | The staff's conversations (TASK 07b, T-24); its own database (`chatwoot-db`) and redis; opened from the panel without a second password | core |
| PostgreSQL 16.15 | 127.0.0.1:5432 | Databases `n8n` and `office` | core |
| Redis 7.4.11 | 127.0.0.1:6379 | Debounce, dedup, locks, rate limits (later tasks) | core |
| Garage v2.4.1 (S3) | http://127.0.0.1:3900 | Object storage: `office-media`, `office-sensitive`, `office-backups` | core |
| meta-stub (.NET, test only) | none | Stand-in for Meta media and OpenAI transcription during `hazem-e2e` | `tests` profile, started by `hazem-e2e` only |
| WAHA noweb-2026.9.2 (test channel) | http://127.0.0.1:3010 (dashboard, QR code) | TASK 02b: WhatsApp on a **spare number** until Meta approves; n8n uses it only while `whatsapp.provider` = `waha` (docs/runbooks/waha.md) | `waha` profile, by hand only |
| cloudflared 2026.10.4 | public https URL | Exposes **only Meta's webhook** (through `edge`) | `tunnel` / `tunnel-named` profile |

Every port is bound to `127.0.0.1`. Nothing is reachable from the network except n8n,
and only while a tunnel profile is running. Appsmith was removed in TASK 07a (the panel is React).

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
7. **Create the first panel manager** (once): `docker compose exec -it api dotnet
   /app/cli/Office.Cli.dll create-user --name <user> --display "<name>" --email <e-mail> --role manager`
   and type the password in the terminal. Then sign in at http://127.0.0.1:8080
   ([docs/runbooks/panel.md](docs/runbooks/panel.md)).
8. **Set up Chatwoot** (once, and again after new `CHATWOOT_*` tokens): `chatwoot-setup`
   ([docs/runbooks/chatwoot.md](docs/runbooks/chatwoot.md)).

## Everyday commands

| Script | What it does |
|---|---|
| `up` | Start core services and wait until healthy |
| `up-tunnel` | Core + Cloudflare tunnel, prints the public URL |
| `down` | Stop everything, keep all data |
| `status` | Containers, health, ports |
| `logs <service> [lines]` | Follow one service's logs |
| `healthcheck` | Full check (containers, ports, DB roles, n8n, Redis, buckets, keys, migrations) |
| `migrate up\|down\|status` | Run migrations on `office` as `office_migrator` |
| `n8n-import` | Credentials from `.env` + workflows into n8n; restarts n8n when a schedule or webhook was published |
| `wa-tests` | WhatsApp database rules + n8n harness (no Meta) |
| `backend-test [-Docker]` | .NET tests; uses a throwaway database (needs `up`) |
| `web-test` | The panel's tests (type-check, Vitest, build; prints the bundle size, no limit) in a Node container |
| `hazem-e2e` | Whole loop n8n ↔ API without Meta or OpenAI (stub, dry run), including Hazem's TASK 04 answers, then restores every setting and removes its temporary credentials |
| `backup` | Dump `office`, `n8n` and `chatwoot` to `office-backups` |
| `chatwoot-setup` | Chatwoot's account, inbox, «حازم (النظام)» and Platform App from the `CHATWOOT_*` tokens; safe to run again (TASK 07b) |
| `restore ...` | Restore a dump into a new database and verify it |
| `reset` | **Deletes all data.** Only with `ENVIRONMENT=test` and after typing `RESET` |
| `forget-number <number>` | **Test only.** Hazem forgets one number (backup first): its data, its bookings' Google Calendar events, its Chatwoot contact and Redis keys; it stays allowed ([docs/runbooks/whatsapp.md](docs/runbooks/whatsapp.md#forget-one-number-test-only)) |
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
  dashboard (free account), add a public hostname pointing to `http://edge:8081` (TASK 13: never
  `http://n8n:5678`; the edge lets through Meta's webhook only), put the
  token in `CLOUDFLARE_TUNNEL_TOKEN`, and set `WEBHOOK_URL=https://<your-hostname>/`.
  The URL then stays the same.

The tunnel forwards to n8n only. PostgreSQL, Redis, Garage, the API and the panel are never exposed.

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

### Nightly job (TASK 13)

One job every night at 03:00: the backup (the three databases **and the client files**, encrypted with
`BACKUP_ENCRYPTION_KEY`), the healthcheck, a scan of the last day's logs, and once a month a restore drill. Each
step is recorded, and a failed one reaches the managers (the panel's bell and an e-mail).

**Windows (Task Scheduler).** Docker Desktop must be running at 03:00. Replace `<REPO>` with the full path of this
folder (here `K:\law-office-whatsapp`):

```powershell
schtasks /Create /SC DAILY /ST 03:00 /TN "OfficeWhatsAppNightly" /TR "powershell -NoProfile -ExecutionPolicy Bypass -File \"<REPO>\scripts\windows\nightly.ps1\""
```

If an older `OfficeWhatsAppBackup` task exists, remove it (`schtasks /Delete /TN "OfficeWhatsAppBackup"`): the
nightly job includes the backup.

**Linux (cron):**

```bash
0 3 * * * /path/to/repo/scripts/linux/nightly.sh >> /var/log/office-nightly.log 2>&1
```

The dumps and the files manifests are kept 30 days; a file deleted from the office stays in the backup 30 days.
**Keep a copy of `BACKUP_ENCRYPTION_KEY` outside this machine**: without it no backup can be read. A copy of the
backups off this disk: `scripts/windows/backup-export.ps1 -To E:\OfficeBackups`. Restoring:
[docs/runbooks/backup-restore.md](docs/runbooks/backup-restore.md). Keys: [docs/runbooks/secrets.md](docs/runbooks/secrets.md).

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
