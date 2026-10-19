# Office WhatsApp System (Hazem) — Test/Staging

WhatsApp assistant ("Hazem") for the law office of Ustaz Saad Fathy Saad.
**ASP.NET Core (.NET 10)** backend, **PostgreSQL 16**, **Redis**, S3-compatible storage, and
**Meta WhatsApp Cloud API**. The React panel arrives in TASK 07.

- Decisions (source of truth): [docs/decisions/DECISIONS-v1.1.md](docs/decisions/DECISIONS-v1.1.md)
- Architecture: [docs/architecture.md](docs/architecture.md) · Infrastructure: [docs/infrastructure.md](docs/infrastructure.md)
- WhatsApp integration: [docs/whatsapp-integration.md](docs/whatsapp-integration.md) · Runbook: [docs/runbooks/whatsapp.md](docs/runbooks/whatsapp.md)
- Meta setup (project owner): [docs/meta-setup.md](docs/meta-setup.md) · Environment: [docs/env-variables.md](docs/env-variables.md)
- Backend code: [backend/README.md](backend/README.md)

## Services

| Service | Local URL / port | Purpose | Starts with |
|---|---|---|---|
| api (ASP.NET Core 10) | http://127.0.0.1:8081 | WhatsApp webhook, sending, background jobs (Hangfire) | core |
| PostgreSQL 16.15 | 127.0.0.1:5432 | Database `office` | core |
| Redis 7.4.11 | 127.0.0.1:6379 | Debounce, locks, rate limits (TASK 03+) | core |
| Garage v2.4.1 (S3) | http://127.0.0.1:3900 | `office-media`, `office-sensitive`, `office-backups` | core |
| cli (Office.Cli) | — | migrations, Meta webhook, templates, test sends | `tools` profile |
| cloudflared 2026.9.3 | public https URL | Exposes **the API only** for the Meta webhook | `tunnel` profile |

Every port is bound to `127.0.0.1`. Only the API is reachable from the internet, and only
while the tunnel runs.

## Requirements

- **Windows:** Docker Desktop (WSL2). Optional: .NET 10 SDK (tests run in a container without it).
- **Linux (VPS later):** Docker Engine with the Compose plugin.

Every script exists as `scripts\windows\<name>.ps1` and `scripts/linux/<name>.sh`.
Windows PowerShell may block local scripts; run them as:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\windows\up.ps1
```

## First run (from zero)

1. `Copy-Item .env.example .env`
2. Run `gen-secrets` and paste the printed lines over the `CHANGE_ME` values in `.env`.
3. `up` — builds the API image and starts the core services (refuses while `.env` has `CHANGE_ME`).
4. `migrate` — applies EF Core migrations and the Hangfire schema as `office_migrator`.
5. `healthcheck` — every line must say `PASS`.
6. WhatsApp: follow [docs/meta-setup.md](docs/meta-setup.md), then `tunnel-refresh`.

## Everyday commands

| Script | What it does |
|---|---|
| `up` / `down` | Start core services (and wait until healthy) / stop everything, keep data |
| `status`, `logs <service>` | Containers and health / follow one service's logs |
| `healthcheck` | Full check: containers, ports, DB roles, API, Redis, buckets, keys, migrations |
| `migrate [up\|status]` | EF Core migrations + Hangfire schema (as `office_migrator`) |
| `cli <command>` | Office.Cli: `wa-check`, `wa-subscribe`, `templates-submit`, `templates-sync`, `send-test` … |
| `backend-build` | Build the API/CLI image |
| `backend-test [--docker]` | Unit + integration tests (throwaway `office_test_*` database) |
| `tunnel-refresh` / `tunnel-down` | Start the Quick Tunnel and register it at Meta / stop it |
| `backup`, `restore …` | Dump `office` to `office-backups` (30 days) / restore into a new DB and verify |
| `reset` | **Deletes all data.** Only with `ENVIRONMENT=test` and after typing `RESET` |
| `gen-secrets` | Print fresh random secrets (never edits `.env`) |

## Quick Tunnel (no domain yet)

The webhook runs on a Cloudflare Quick Tunnel (DECISIONS v1.1, D-5). Its URL changes every
time the tunnel, Docker or the machine restarts, so:

- **Start of every test session:** `tunnel-refresh` — starts the tunnel and registers the new
  URL at Meta automatically.
- **End of the session:** `tunnel-down`.
- Messages sent to the number while no tunnel runs may be lost after Meta's retries.

Production needs a real domain (T-13).

## Daily backup

**Windows (Task Scheduler), 03:00 daily** (Docker Desktop must be running):

```powershell
schtasks /Create /SC DAILY /ST 03:00 /TN "OfficeWhatsAppBackup" /TR "powershell -NoProfile -ExecutionPolicy Bypass -File \"K:\law-office-whatsapp\scripts\windows\backup.ps1\""
```

**Linux (cron):** `0 3 * * * /path/to/repo/scripts/linux/backup.sh >> /var/log/office-backup.log 2>&1`

## Troubleshooting

| Problem | Fix |
|---|---|
| `port is already allocated` | Another program uses 5432/6379/3900/8081. Stop it or change the left side of the mapping (keep `127.0.0.1:`). |
| `preflight` fails | `.env` has a placeholder or malformed value; the message names it. |
| Docker commands hang | Docker Desktop needs free space on C: and K:. Quit and reopen Docker Desktop. |
| API unhealthy | `logs api`. Usually a missing `WhatsApp__*` value while `WhatsApp__Enabled=true`. |
| `bad interpreter` / `\r` errors | A `.sh` file got CRLF endings; `.gitattributes` forces LF — re-checkout the file. |
| Git Bash rewrites `/app/...` paths | Prefix with `MSYS_NO_PATHCONV=1` (the scripts already do). |
