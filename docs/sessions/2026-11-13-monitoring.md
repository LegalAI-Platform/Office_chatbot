# 2026-11-13 — v1.107: the monitoring (Prometheus, Alertmanager, Loki + Alloy, Grafana)

Mohamed Emad's session, started from his main session. The owner asked for a production-ready monitoring stack with Docker
Compose on the test server, mapped to this project (PostgreSQL ×2, Redis ×2, the .NET API, n8n, Chatwoot and Sidekiq). The
plan was approved («موافق، ابدأ») with the recommended answers: Grafana through an SSH tunnel only, Prometheus keeps 30 days,
the alert e-mails go into `.env` when it is turned on. Branch `feat/monitoring` (worktree `K:\wt\monitoring`), from `main` at
v1.106 (6d3ff67).

## What was done

| Part | Where |
|---|---|
| Profile `monitoring`: 13 services, pinned images, read-only, no capabilities, memory and process limits; networks `monitoring_net` and `docker_proxy_net` (internal), `monitoring_out` (Alertmanager's mail, Grafana's port) | `docker-compose.yml` |
| The API's metrics: OpenTelemetry (requests, .NET runtime, Npgsql, calls out) on port 9464 only; `AlertMetrics` (the bell's rounds and e-mails) | `backend/src/Office.Api/Program.cs`, `OfficeMetrics.cs`, `Office.Infrastructure/Monitoring/AlertMetrics.cs`, `Dockerfile` |
| n8n's `/metrics` | `N8N_METRICS*` in `docker-compose.yml` |
| Prometheus: 15 s, 30 days / 8 GB, 41 alert rules in Arabic, their tests | `infra/monitoring/prometheus` |
| Alertmanager: e-mail through the panel's SMTP, resolved, inhibition, Arabic RTL template | `infra/monitoring/alertmanager` |
| Loki 7 days; Alloy masks phones (last 3 digits kept), passwords, tokens, keys, and counts the log-scan's patterns | `infra/monitoring/loki`, `infra/monitoring/alloy` |
| Telegraf: the containers from Docker's API through the read-only docker-proxy | `infra/monitoring/telegraf` |
| Grafana: 3 data sources, 6 dashboards | `infra/monitoring/grafana` |
| `monitoring-init`: checks `.env`, makes `office_monitor` / `chatwoot_monitor` (`pg_monitor`), passwords in a volume | `infra/tools/bin/monitoring-init` |
| healthcheck: the API's metrics port only on 9464, the panel and the edge never pass `/metrics`, and the monitoring (ready, rules, targets, Alertmanager and its e-mails, Loki, the bell's rounds); the containers' state on the host | `infra/tools/bin/healthcheck`, `scripts/*/healthcheck.*` |
| `monitoring.sh` / `monitoring.ps1`: up, down, status, test-email, check, grafana-password | `scripts/linux`, `scripts/windows` |
| `deploy.sh` keeps the monitoring on each version (and back) | `deploy/deploy.sh` |
| CI job «Monitoring config» | `.github/workflows/ci.yml` |
| Docs | `DECISIONS-v1.107.md` (T-16, T-25), `docs/runbooks/monitoring.md`, `server.md` §10, `env-variables.md`, README, CLAUDE.md, START-HERE |

## Checked (here, on K:)

- `dotnet build backend\Office.sln`: no warning. The new unit test (AlertMetrics) and the two integration tests (`/metrics` on
  9464 with the requests, the runtime and Npgsql's pool, no password, token or number; 404 elsewhere) pass against a
  throwaway Postgres.
- The whole stack with the monitoring on Docker Desktop, as project `montest` with its own networks and a throwaway `.env`
  (never the owner's data or networks): every target up, every probe up (`wa.` and the landing page from outside too).
- Every dashboard query run against it (99 with data; the empty ones need traffic, or the disk mount Docker Desktop lacks).
- A made-up number logged by Postgres: masked in Loki (`'********678'`), counted, the alert fired, its e-mail reached the
  mail catcher (mailpit), then «✅ اتحلت». `monitoring.sh test-email`: both e-mails, Arabic subjects encoded right.
- `monitoring.sh check`: promtool (config, 41 rules, the rules' tests), amtool, Loki, Alloy (validate and the masking test,
  16 checks), Telegraf, blackbox, the dashboards. The healthcheck's monitoring checks all pass.

## Deviations, and why

- **cAdvisor → Telegraf.** cAdvisor 0.60 needs containerd's socket when Docker uses the containerd image store, as the
  server does (read-only check: `io.containerd.snapshotter.v1`). That socket is root on the machine. Telegraf reads the same
  numbers (and the healthchecks, the restarts) from Docker's API through the read-only proxy.
- **Grafana 13.3.0, not 13.3.1.** 13.3.1 (security fixes) was published 2026-11-10: it passes the two-week rule on 2026-11-24.
- **node-exporter needs `rslave`** (Docker refuses a mount of `/` without it); Docker Desktop has none, so `monitoring.ps1`
  starts it without node-exporter.
- **The alert counters' first value**: Alloy's and the API's counters start at their first line or failure, which
  `increase()` alone misses; the rules add an `unless … offset` half (tested).

## Not done / next

- Not deployed. After the merge and the deploy, the owner turns it on (runbook «Turning it on»): two `.env` lines,
  `monitoring.sh up`, `monitoring.sh test-email`.
- On the server for the first time: the disk metrics (node-exporter with the real `/`), the real traffic's dashboards.
- An outside heartbeat (the server itself down) and Grafana from a phone (Cloudflare Access) are not done: decisions of their own.
