# The monitoring (v1.107)

What it is and why: [`DECISIONS-v1.107.md`](../decisions/DECISIONS-v1.107.md) (T-16, T-25). This file is the commands, and
what to do when an alert e-mail comes. Every alert e-mail links to its section below («اعمل إيه»).

Two systems, no alert in both:

| | The bell in the panel (since TASK 13) | The monitoring (this file) |
|---|---|---|
| About | the office's work and the nightly jobs: a failed backup, healthcheck or log scan, n8n failures in an hour, the AI down, a client's turn set aside | the server, the containers, both databases and both Redis, the API's requests and runtime, n8n, Chatwoot and Sidekiq, the public links, the logs |
| Who hears | the managers and the maintenance account (the bell and an e-mail) | `ALERT_EMAIL_TO` (e-mail) |
| Lives in | the API (`AlertWorker`) | its own containers (profile `monitoring`) |

Each watches the other: the monitoring alerts when the bell's rounds stop (`AlertWorkerStalled`) or its e-mails fail
(`AlertEmailsFailing`); the healthcheck (the deploy's and the nightly job's) checks the monitoring and its e-mails, and a
failed healthcheck reaches the bell.

## What runs

All in `docker-compose.yml`, profile `monitoring`, never started by `up`. Every image is pinned (published at least two
weeks before it was chosen). Every container is read-only, has no Linux capabilities, no-new-privileges, and a ceiling on
memory and processes.

| Service | What it does | Networks | Host port |
|---|---|---|---|
| `prometheus` | reads every target every 15 s, keeps `PROMETHEUS_RETENTION` (30 days) within `PROMETHEUS_RETENTION_SIZE` (8 GB), evaluates the alert rules (`infra/monitoring/prometheus/rules`) | monitoring_net, office_net | 127.0.0.1:9090 |
| `alertmanager` | the e-mails (through the panel's SMTP: Resend), resolved e-mails, grouping, inhibition | monitoring_net, monitoring_out | 127.0.0.1:9093 |
| `grafana` | the 6 dashboards (`infra/monitoring/grafana`), its own sign-in | monitoring_net, monitoring_out | 127.0.0.1:3300 |
| `loki` | the logs, 7 days | monitoring_net | — |
| `alloy` | reads every container's log, masks it, sends it to Loki | monitoring_net, docker_proxy_net | — |
| `docker-proxy` | Docker's API, reads only (the containers, their stats and logs, the networks, info); every POST refused | docker_proxy_net | — |
| `telegraf` | every container's CPU, memory, network, disk, healthcheck, restarts (from Docker's API through docker-proxy) | monitoring_net, docker_proxy_net | — |
| `node-exporter` | the server: CPU, memory, disk, load, clock (the host's `/` read-only) | monitoring_net | — |
| `postgres-exporter`, `chatwoot-db-exporter` | both Postgres, as `office_monitor` / `chatwoot_monitor` (`pg_monitor` only) | monitoring_net + office_net / chatwoot_net | — |
| `redis-exporter`, `chatwoot-redis-exporter` | both Redis; Chatwoot's with Sidekiq's queues | monitoring_net + office_net / chatwoot_net | — |
| `blackbox` | the HTTP checks: each service's health page from inside, `wa.` and the landing page from outside | monitoring_net, office_net, chatwoot_net | — |
| `monitoring-init` | one-shot: checks `.env`, makes the two monitor roles (their passwords in the `monitoring_secrets` volume, never in `.env`) | office_net, chatwoot_net | — |

Also read by Prometheus: the API on `api:9464` (OpenTelemetry: requests, .NET runtime, Npgsql's pool, the calls out, the
bell's rounds) and n8n on `n8n:5678/metrics` (`N8N_METRICS`).

### Security

- **Nothing is on the internet.** No monitoring service joins `tunnel_net`, so no Cloudflare route can reach one. The edge
  answers 404 for `/metrics`. The panel's nginx sends `/api/panel` alone to the API, and the API serves `/metrics` on 9464
  only (8080 answers 404; 9464 answers nothing else). The healthcheck checks all three.
- **Host ports on 127.0.0.1 only** (Grafana, Prometheus, Alertmanager), reached from the PC through an SSH tunnel. The
  healthcheck fails if any port is published on all interfaces.
- **Loki and Alertmanager are not on office_net**, where n8n (internet-facing through the webhook) is: n8n cannot read the
  logs or add a silence. `monitoring_net` and `docker_proxy_net` are internal (no way out); Alertmanager's mail and
  Grafana's port go through `monitoring_out`.
- **No Docker socket and no containerd socket** in the monitoring: `docker-proxy` holds Docker's socket and answers reads
  only. (cAdvisor was the first choice; with Docker's containerd image store it needs containerd's socket, which is root.)
- **The database roles** see statistics only (`pg_monitor`), 3 connections at most, 10 s per query. Their passwords are
  random, made once by `monitoring-init`, readable by the exporters only.
- **No secret in a file of this repo**: the alert addresses and Grafana's password are in `.env` (typed by the owner); the
  SMTP key is the panel's own `SMTP_PASSWORD`, given to Alertmanager in a file on its `/tmp` (memory).
- **Logs are masked before Loki keeps them** (below), and the metrics hold no client data: a request is known by its route's
  template, never its address or query (the API's integration test proves no password, token or number is in them).
- Grafana: no sign-up, no anonymous access, no basic auth, nothing sent to grafana.com (no usage report, update or news
  check, no plugin installed).

### The logs and the masking

Alloy reads the logs of every container of the project through `docker-proxy` and, before Loki keeps a line, masks:
- a full Egyptian mobile number, except its last 3 digits, like the API's own `LogMask.Phone` (`********678`);
- private keys, bearer and basic tokens, the known key shapes (Meta, OpenAI/Anthropic, Resend, GitHub, Google, JWTs,
  Telegram), the value after `password` / `secret` / `token` / `api_key` / `cookie` / `authorization` …, and a password
  inside a URL.

It also counts, before masking, the lines the nightly log-scan would fail on (the same two patterns, the same services), so
`LogsHavePhoneNumber` / `LogsHaveSecret` come within minutes instead of the next night. Docker's own log still has the line:
the code that logged it is what must change. The masking is one module (`infra/monitoring/alloy/mask.alloy`); its test
(`infra/monitoring/alloy/test/run.sh`, in the CI) sends made-up secrets through it and runs the log-scan's patterns on what
comes out.

## Turning it on (the owner, once, on the server)

1. Deploy this version (`deploy/deploy.sh`): the API's metrics port and n8n's `N8N_METRICS` come with it (n8n restarts once).
2. Two lines in `.env`, typed by the owner (nothing is printed):

   ```bash
   cd /opt/office
   read -rp 'Alert e-mails (comma separated): ' v && echo "ALERT_EMAIL_TO=$v" >> .env; unset v
   read -rsp 'Grafana password (12 characters at least): ' v && echo && echo "GRAFANA_ADMIN_PASSWORD=$v" >> .env; unset v
   ```

   Read-only check (counts, never the values):

   ```bash
   grep -c '^ALERT_EMAIL_TO=.*@' .env; awk -F= '/^GRAFANA_ADMIN_PASSWORD=/ { print length($2) }' .env
   ```

3. Start it: `bash scripts/linux/monitoring.sh up` (about a minute; it pulls ~1.5 GB of images the first time).
4. `bash scripts/linux/monitoring.sh test-email`: an e-mail «🔴 مشكلة: MonitoringTestEmail — تجربة: إيميلات المراقبة شغالة»
   comes within a minute, and «✅ اتحلت» about 5 minutes later. Nothing in a few minutes: [AlertmanagerEmailFailing](#alertmanageremailfailing).
5. `bash scripts/linux/healthcheck.sh`: «The monitoring's containers» and «The monitoring» pass.

From then on every `deploy/deploy.sh` keeps it on the new version (a commit that changed `infra/monitoring` recreates it),
and going back to an earlier version takes it back too.

## Opening Grafana (an SSH tunnel)

From the PC (PowerShell), keep this window open while you look:

```powershell
ssh -N -L 3300:127.0.0.1:3300 -L 9090:127.0.0.1:9090 -L 9093:127.0.0.1:9093 office@<server>
```

- Grafana: http://127.0.0.1:3300 — user `admin` (or `GRAFANA_ADMIN_USER`), password `GRAFANA_ADMIN_PASSWORD`. The home
  page is «Hazem · Overview»; the others are in the folder «Hazem» (and in every dashboard's «Dashboards» menu).
- Prometheus (its targets, the alerts' state): http://127.0.0.1:9090/targets, http://127.0.0.1:9090/alerts
- Alertmanager (the alerts now, the silences): http://127.0.0.1:9093

If the PC runs its own monitoring on 3300, use another local port (`-L 13300:127.0.0.1:3300`).

## The dashboards

All in English (v1.109), one look for all six: a row of status tiles (UP green, DOWN red), a row of key numbers coloured by
the alert rules' own thresholds, then the graphs.

| Dashboard | What is on it |
|---|---|
| Hazem · Overview | the 12 services up or down; firing alerts, CPU, memory, disk, the API's requests, errors and latency, Hazem's reply time, the office database's connections, Redis memory, error and masked log lines; the firing alerts and since when; every service's availability over time; CPU, memory and the API's requests |
| Hazem · Server & Containers | CPU, memory, disk, load, uptime, the clock; their graphs; the «Containers» table (state, healthcheck, restarts in 24 h, CPU, memory, memory of its limit); CPU, memory, network and disk writes per container |
| Hazem · API | requests, errors, latency, Hazem's replies, requests in flight, connections; the busiest and the slowest routes, 5xx by route; .NET: CPU, memory, GC, thread pool, exceptions; Npgsql's pool and commands; the calls out (OpenAI, Anthropic, Chatwoot, n8n, Google); the bell's alert worker |
| Hazem · Databases | both PostgreSQL: up, connections (of the maximum, by state, by database), transactions, cache hits, sizes, the longest transaction, locks, deadlocks; both Redis: up, memory, clients, commands, keys |
| Hazem · Services | n8n: up, active workflows, event loop, memory, CPU, failed workflows; Chatwoot and Sidekiq: up, response, queued jobs, retries, dead jobs, jobs per minute; the storage |
| Hazem · Logs | lines, error lines, masked and dropped lines; lines and error lines by service; the logs themselves (pick a service, search) |

After v1.109's deploy the dashboards are in a new folder «Hazem», and v1.107's «حازم» is left empty: delete it once (Grafana
→ Dashboards → «حازم» → Folder actions → Delete).

They are made by `infra/monitoring/grafana/make-dashboards.py` (one file for all six), read again by Grafana every minute,
and cannot be saved from Grafana. To change one: change the script, run `python3 infra/monitoring/grafana/make-dashboards.py`,
commit the script and the JSON, deploy. The CI fails when the JSON is not what the script writes.

## The alerts

Rules: `infra/monitoring/prometheus/rules/*.yml` (Arabic summaries and descriptions), tested by
`infra/monitoring/prometheus/tests/alerts.yml`. Routing and inhibition: `infra/monitoring/alertmanager/alertmanager.yml`.

- **critical** (now): e-mailed after 30 s, again every 4 hours while it lasts. **warning** (today): after 1 minute, again
  once a day. Every alert says once more when it is resolved («✅ اتحلت»).
- One e-mail per kind of alert and service. Resend's free plan sends 100 e-mails a day, the panel's sign-in codes included:
  this is why the repeats are rare.
- **Inhibition** (an alert that explains another silences it): a critical alert silences the warnings of the same service; a
  stopped container (`ContainerExited`) silences its service's own check; the office's database down silences the API's
  and n8n's alerts; Chatwoot's database or Redis down silences Chatwoot's and Sidekiq's; the API down silences the bell's
  and the pool's; the panel's web down silences the landing page from outside; the server nearly out of memory silences the
  restarts and the slow answers.
- **A silence** (planned work, a known problem): Alertmanager → «New Silence», a matcher (e.g. `service="chatwoot"`), an
  end time and a comment. Or on the server:
  `docker compose --profile monitoring exec alertmanager amtool silence add 'service="chatwoot"' --duration=2h --comment="…" --alertmanager.url=http://127.0.0.1:9093`

### What each alert means

<a id="test-email"></a>**MonitoringTestEmail** — the test of `monitoring.sh test-email`. Nothing to do: it resolves by itself.

#### The server

##### HostHighCpu
«الـ CPU بتاع السيرفر فوق 85%». Grafana «Hazem · Server & Containers»: which container's CPU went up. A quality-eval run or a
burst of messages passes; a container stuck at 100% for an hour is restarted (`docker compose restart <service>`).

##### HostLowMemory
«الرام الفاضية في السيرفر أقل من 15%». Which container's memory grows (same dashboard). Chatwoot and n8n take the most.
`docker compose restart <service>` frees it for a while; if it keeps growing, it is a leak to report.

##### HostOutOfMemory
«السيرفر قرّب يخلص الرام». Act now: Linux will kill a container soon (even Postgres). Restart the biggest one that is not the
database, then find out why (above). The server has 2 GB of swap: when swap is full too, this follows.

##### HostOomKill
«Linux قفل برنامج عشان الرام خلصت». See which container restarted (`ContainerRestarting`, the «Containers» table of «Hazem · Server & Containers»), and why it used so much.

##### HostDiskSpaceLow
«الديسك فاضي فيه أقل من 15%». Usually old images: `docker image prune -a` (removes images no container uses; deploy.sh pulls
what it needs again). `docker system df` shows what takes the space.

##### HostDiskSpaceCritical
«الديسك قرّب يتملي». Act now: when it is full, Postgres and the backup stop. `docker image prune -a` first, then
`docker system df`, then `du -xh / --max-depth=2 | sort -h | tail`.

##### HostDiskWillFillIn24h
«الديسك هيتملي في خلال يوم بالسرعة دي». Something writes a lot: «Disk writes by container» («Hazem · Server & Containers») says who.

##### HostInodesLow
«عدد الملفات على الديسك قرّب يخلص». Many small files: `docker system prune` (stopped containers, build cache), then look for a
folder with millions of files.

##### HostClockNotSynced
«ساعة السيرفر مش مظبوطة». The authenticator codes and the booking depend on it. `timedatectl` (as root) shows the sync;
`systemctl restart systemd-timesyncd` usually fixes it.

#### The containers

##### ContainerExited
«الـ container … واقف». `docker compose ps -a` and `docker compose logs --tail 100 <service>`; then `docker compose up -d
<service>`. Only the services the office runs on page (v1.109): the default profile's, the edge and the Named Tunnel. A
profile's container stopped on purpose never does (the old Quick Tunnel, WAHA, the test stub), nor a one-shot job that
finished or a `docker compose run`. A needed service you stop on purpose still pages: silence it in Alertmanager for the work.

##### ContainerUnhealthy
«Docker شايف الـ container … مش healthy». Its own healthcheck fails (`docker inspect --format '{{json .State.Health}}'
office-<service>-1`). Its logs say why; a restart is the first try.

##### ContainerRestarting
«الـ container … بيقع ويقوم تاني». It crashes and Docker starts it again: its last logs before each start say why (Grafana
«Hazem · Logs», the service). Out of memory (`ContainerMemoryNearLimit`) is a common reason.

##### ContainerMemoryNearLimit
«الـ container … قرّب يوصل لحد الرام بتاعه». At its `mem_limit` Linux kills it. If it is normal growth, raise its `mem_limit`
in `docker-compose.yml` (a commit); if it keeps growing, it is a leak.

#### The databases

##### PostgresDown
«داتابيز المكتب / Chatwoot مش بترد». `docker compose ps postgres` (or `chatwoot-db`), its logs; `docker compose up -d
postgres`. The office's: Hazem, the panel and n8n stop with it (n8n keeps the messages and retries, T-20).

##### PostgresTooManyConnections
«… قرّبت توصل لأقصى عدد اتصالات». Grafana «Hazem · Databases»: who holds them («Connections by database»). The API's pool can grow to 100.

##### PostgresDeadlocks
«فيه deadlock». Postgres cancelled one of two transactions; the code retries. Once is nothing; many in a day is a bug to report
(Grafana «Hazem · Logs», postgres, `deadlock detected`).

##### PostgresLongTransaction
«فيه transaction مفتوحة بقالها أكتر من ربع ساعة». It holds locks and stops vacuum. Find it:
`docker compose exec postgres psql -U "$POSTGRES_SUPERUSER" -d postgres -c "SELECT pid, usename, state, now() - xact_start AS age, left(query, 60) FROM pg_stat_activity WHERE xact_start < now() - interval '15 minutes'"`
and, if it is stuck, `SELECT pg_terminate_backend(<pid>)`.

##### RedisDown
«Redis بتاع المكتب / Chatwoot مش بيرد». `docker compose ps redis` (or `chatwoot-redis`), its logs, `up -d`.

##### RedisMemoryHigh
«Redis … مستخدم أكتر من 80% من حده». The office's Redis has 256 MB and refuses writes when full (noeviction): n8n's grouping
stops. Its keys: `docker compose exec redis redis-cli --scan | cut -d: -f1 | sort | uniq -c | sort -rn | head`.

##### RedisRejectedConnections
«Redis … رفض اتصالات». More clients than it allows: a service opens connections and never closes them (its logs).

#### The API (Hazem)

##### ApiDown
«الـ API (حازم) مش بيرد». `docker compose ps api`, `docker compose logs --tail 100 api`, `docker compose up -d api`. n8n keeps
the messages and retries every minute (T-20).

##### ApiHighErrorRate
«أكتر من 5% من طلبات الـ API بترجع خطأ (5xx)». Grafana «Hazem · API»: «5xx by route» says which route; «Hazem · Logs», api, the errors.

##### ApiSlowRequests
«البانل بطيء». «Slowest routes (p95)» («Hazem · API») says which; then the database (`PostgresLongTransaction`, the pool) or the server (CPU).

##### HazemTurnsSlow
«حازم بياخد أكتر من 45 ثانية علشان يرد». Usually the AI: «Calls out» («Hazem · API») shows api.anthropic.com / api.openai.com slow
or failing. The bell's «الـ AI مش شغال» comes when it fails outright.

##### ApiDbPoolNearFull
«اتصالات الـ API بالداتابيز قرّبت تخلص». Requests wait for a connection: a slow query or a long transaction holds them.

##### ApiThreadPoolQueue
«الـ API عنده شغل مستني في الـ thread pool». Something blocks threads; a restart clears it, the cause is a bug to report.

##### AlertWorkerStalled
«تنبيهات الجرس واقفة». The bell and its e-mails stopped: «Hazem · Logs», api, `Alerts round failed`. Most often the database (then
`PostgresDown` says so); otherwise `docker compose restart api`.

##### AlertEmailsFailing
«إيميلات البانل مش بتتبعت». The panel's mail (Resend) refuses: the same key sends the sign-in codes. «Hazem · Logs», api,
`e-mail(s) not sent`; Resend's dashboard (the key, the quota); the key is changed with `deploy/set-smtp-resend.sh`.

#### n8n, the panel, Chatwoot, storage, outside

##### N8nDown
«n8n واقف». No WhatsApp message comes in or goes out. `docker compose ps n8n`, its logs, `docker compose up -d n8n`. Meta
sends the messages again when n8n is back.

##### N8nEventLoopLag
«n8n مزنوق». A workflow runs something heavy, or n8n is short of CPU or memory. A restart clears it.

##### WebDown
«البانل مش بيفتح». `docker compose ps web`, its logs, `docker compose up -d web`.

##### ChatwootDown
«Chatwoot مش بيفتح». The staff cannot answer; Hazem still works. `docker compose ps chatwoot`, its logs, `up -d chatwoot`.

##### SidekiqNotRunning
«Sidekiq بتاع Chatwoot مش شغال». Messages do not move between Chatwoot and the API. `docker compose ps chatwoot-sidekiq`, its
logs, `docker compose restart chatwoot-sidekiq`.

##### SidekiqBacklog
«شغل Chatwoot متأخر». Sidekiq does not keep up: CPU, memory, or one job failing again and again (its logs).

##### SidekiqDeadJobs
«شغل في Chatwoot فشل نهائي». Chatwoot's own page: `https://chat.<domain>/monitoring/sidekiq` (super admin) → «Dead».

##### GarageDown
«التخزين (Garage) مش بيرد». `docker compose ps garage`, its logs, `up -d garage`. n8n cannot keep new files meanwhile.

##### PublicEndpointDown
«… مش بيفتح من برّه». From the server through Cloudflare: `wa.` (Meta's webhook) or the landing page. `docker compose
--profile tunnel-named ps` (the tunnel and the edge), `docker compose --profile tunnel-named logs --tail 50 tunnel-named`,
Cloudflare's dashboard (the tunnel's state). `panel.` and `chat.` are not checked: Cloudflare lets Egypt only reach them.

#### The logs

##### LogsHavePhoneNumber
«لوجز … فيها رقم موبايل كامل». Masked in Loki, but Docker still holds the line, so the night's log-scan will fail. Find it:
Grafana «Hazem · Logs», the service, search `****` (the masked number). The code that logged it must change
(`LogMask.Phone`, or no literal in a SQL that can fail: a subquery).

##### LogsHaveSecret
«لوجز … فيها مفتاح أو توكن». Treat the key as seen: change it (`docs/runbooks/secrets.md`), then fix what logged it.

##### LogShippingFailing
«لوجز مش بتوصل لـ Loki». `docker compose --profile monitoring logs --tail 50 loki alloy`.

#### The monitoring itself

##### TargetDown
«Prometheus مش قادر يقرا من …». `bash scripts/linux/monitoring.sh status` (the containers, the targets that do not answer);
`docker compose --profile monitoring up -d <service>`.

##### PrometheusRuleFailures
«Prometheus مش قادر يحسب بعض التنبيهات». A rule's query is wrong for the data it gets: http://127.0.0.1:9090/rules shows it.
Fixed in `infra/monitoring/prometheus/rules` (with its test), committed, deployed.

##### AlertmanagerEmailFailing
«إيميلات المراقبة مش بتتبعت». This may not reach you by e-mail, since it is the e-mail that fails: the healthcheck checks it
too, and the bell shows a failed healthcheck. `docker compose --profile monitoring logs --tail 50 alertmanager`: a refused key
(the panel's `SMTP_PASSWORD`, `deploy/set-smtp-resend.sh`), Resend's daily quota, or `ALERT_EMAIL_TO`.

## Day to day

- `bash scripts/linux/monitoring.sh status` — the containers, and the targets that do not answer.
- `bash scripts/linux/monitoring.sh down` / `up` — stop it (its data stays) / start it again. While it is stopped, the
  healthcheck skips its checks.
- `bash scripts/linux/monitoring.sh grafana-password` — Grafana's admin password set again from `GRAFANA_ADMIN_PASSWORD`
  (Grafana keeps it in its own database from its first start; changing `.env` alone changes nothing).
- New passwords for the two monitor roles: `docker compose --profile monitoring down` is not needed; remove the volume
  (`docker compose --profile monitoring rm -sf postgres-exporter chatwoot-db-exporter monitoring-init && docker volume rm
  office_monitoring_secrets`), then `monitoring.sh up` makes new ones.
- Its disk: Prometheus ~4 GB at 30 days (8 GB at most), Loki under 1 GB (7 days). `docker system df -v | grep -E
  'prometheus|loki'`.
- Its memory: about 1 GB in all.
- `bash scripts/linux/monitoring.sh check` — every config, the alert rules' tests and the masking test (Docker only; the
  CI's «Monitoring config» job runs the same).

## Changing it

- **A threshold or a new alert**: `infra/monitoring/prometheus/rules/*.yml`, and its case in
  `infra/monitoring/prometheus/tests/alerts.yml` (the exact Arabic words too); `monitoring.sh check`; a runbook section here.
- **What is masked**: `infra/monitoring/alloy/mask.alloy`, and a line for it in `infra/monitoring/alloy/test/run.sh`.
- **A version**: the image in `docker-compose.yml`, published at least two weeks before. Known: Grafana 13.3.1
  (security fixes, 2026-11-10) qualifies from 2026-11-24.
- **Who gets the e-mails**: `ALERT_EMAIL_TO` in `.env`, then `monitoring.sh up`.
- **How long Prometheus keeps**: `PROMETHEUS_RETENTION` / `PROMETHEUS_RETENTION_SIZE` in `.env`, then `monitoring.sh up`.
- **Grafana from a phone**: a Cloudflare Access application on a `grafana.` hostname, routed to the edge, with a server
  block there for Grafana: not done (the owner chose the SSH tunnel); a decision of its own.

## Limits

- **If the server itself is down, nothing on it can send an e-mail.** An outside heartbeat (e.g. healthchecks.io, free)
  that expects a ping every minute would; not set up.
- **On Docker Desktop** (the PC) `scripts\windows\monitoring.ps1` starts it without node-exporter: Docker Desktop cannot
  mount the machine's `/` the way it needs, and the "machine" there is Docker Desktop's VM anyway.
- `panel.` and `chat.` are not checked from outside (Egypt only); `wa.` and the landing page are, through the same tunnel.
- Chatwoot (Rails) has no metrics of its own: it is checked by its page, its container and Sidekiq's keys in its Redis.
