# 2026-11-14 — v1.109: Grafana in English, a cleaner look, ContainerExited for the needed services only

Mohamed Emad's session, asked through his main session. He signed in to Grafana on the test server and said: «بس اولا خلي
كل حاجه انجليزي. تاني حاجه الشكل، انا حاسه حاسس ان الشكل مش تمام، يعني اليو اي نفسه». Grafana drew the Arabic titles
reversed and broken, and the 12 services were huge tiles saying «شغال». A false critical alert came too: `ContainerExited`
for the old Quick Tunnel (`office-tunnel-1`, stopped on purpose since the Named Tunnel took over). Branch
`feat/grafana-english` (worktree `K:\wt\grafana-english`), from `main` at b44558c (its tree is v1.107: v1.108 was reverted, #153).

## What was done

| Part | Where |
|---|---|
| The 6 dashboards rewritten in English, one look: compact UP/DOWN tiles, a row of key numbers (the alert rules' thresholds), then graphs; new: the firing alerts and since when, every service's availability over time, a «Containers» table | `infra/monitoring/grafana/dashboards/*.json` |
| The dashboards as code: `make-dashboards.py` writes them; `--check` fails when the JSON differs (in `monitoring.sh check`, so in CI) | `infra/monitoring/grafana/make-dashboards.py`, `scripts/linux/monitoring.sh` |
| The folder «Hazem» (a new uid), Grafana's default language en-US | `provisioning/dashboards/dashboards.yml`, `docker-compose.yml` |
| `ContainerExited` for the services the office runs on only; a test case for the stopped Quick Tunnel and WAHA | `infra/monitoring/prometheus/rules/containers.yml`, `tests/alerts.yml` |
| `monitoring-init` waits up to a minute for each database over TCP (a fresh database's socket healthcheck says ready too early) | `infra/tools/bin/monitoring-init` |
| Docs | `DECISIONS-v1.109.md`, `docs/runbooks/monitoring.md`, CLAUDE.md, START-HERE |

## Checked

- `make-dashboards.py --check`: the JSON is what it writes; no Arabic left; unique panel ids; every panel inside the grid.
- The alert rules' tests (promtool) with the new case passed locally before the local Docker run was stopped.
- The rest (promtool, amtool, Loki, Alloy and the masking test, Telegraf, blackbox, the dashboards' check) runs in the CI's
  «Monitoring config» job.

## Deviations, and why

- **No before/after screenshots from the PC.** Running the whole stack locally made Windows grow its page file on C:
  (`C:\pagefile.sys`, 15 GB allocated; C: went from 8.5 GB free to 219 MB), and the owner said «اشتغل علي k». The local
  stack and Docker Desktop were stopped (C: back to ~5 GB). The new look is seen on the server after the deploy.
- The old folder «حازم» cannot be removed by provisioning: the owner deletes it once (runbook).

## Next

- His OK to merge, then to deploy (deploy.sh recreates the monitoring, since infra/monitoring changed).
- The alert e-mails' wording (Arabic now) waits for his decision.
