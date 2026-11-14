# 2026-11-14 — v1.115: a second deploy within the hour no longer fails on old log lines

Mohamed Emad's session. Deploying v1.114 rolled back twice:
- The first time, the urgent alert's test switch had been turned on again from the panel at 21:01. It was turned off at 21:43 on his «اقفله ونزّل».
- The second time, the healthcheck's «the logs reach Loki (Alloy sent lines in the last hour, none dropped)» failed.

He asked «هو انا ليه ما اعرفش انزله دلوقتي؟». v1.114 went out at 21:57 with the monitoring stopped for the deploy, on «أيوه نزّلها». On the lasting fix he said «أيوه، صلّحها». Branch `fix/alloy-old-lines` (worktree `K:\wt\cw-username`), from `main` at 679ab45.

## What the server showed (read only)

- About 133 lines were dropped in that hour, all with `reason="ingester_error"`, and all in the 10 minutes of the two deploys.
- Loki's log said why: «entry too far behind, entry timestamp is: …T18:28…, oldest acceptable timestamp is: …T18:46…» for the stream `container="office-chatwoot-init-1"`.
- So the one-shot `chatwoot-init`, started again by each deploy and each rollback, still had its 20:28 run in its log. Alloy sent those lines again, and Loki refused the ones more than an hour behind the stream's newest.
- Alloy dropped only lines older than 6 hours. Lines from 1 to 6 hours old went to Loki and were refused there.

## What changed

| What | Where |
|---|---|
| Only the last 50 minutes of a log go to Loki (`stage.drop`, `older_than = "50m"`, the reason `older_than_50m`), so nothing Alloy sends is ever more than an hour behind. Older lines stay in Docker and are counted apart from the lines Loki lost | `infra/monitoring/alloy/config.alloy` |
| What «entry too far behind» means, and the two ways to deploy when such a drop already sits in the last hour: wait, or stop the monitoring for the deploy | `docs/runbooks/monitoring.md` (LogShippingFailing) |
| Docs | DECISIONS-v1.115, CLAUDE.md, START-HERE |

## Checked

- Docker is not running on this PC. Alloy's config check and the masking test run in the CI («Monitoring config»).
- The change is one stage the config already had, with a shorter window.

## For the owner

- The deploy that brings this restarts the monitoring, because its config changed.
- If a deploy fails this way again before then, stop the monitoring for the deploy, as on 2026-11-14 (the runbook says how).
- After an Alloy outage longer than 50 minutes, the gap's older lines stay in Docker only (`docker compose logs`), not in Grafana.
