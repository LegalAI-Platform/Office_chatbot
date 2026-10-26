# Security fixes 1 — 2026-10-26

After the full security review (`docs/security/review-2026-10-26.md`, with its threat model
`docs/security/threat-model.md`), the owner chose «1»: fix the updates and the medium findings that need no decision
from them. Branch `security-fixes-1` from `main` = `913daa4`.

## What changed

| Finding | Change |
|---|---|
| 1. Programs with known vulnerabilities | .NET `aspnet 10.0.13` / `sdk 10.0.403` (alpine3.24), nginx-unprivileged `1.30.6` (web and edge), n8n `2.44.7` (the `stable` tag; 2.45 is `beta`), Chatwoot `v4.19.1-ce`, cloudflared `2026.10.4` |
| 2. A failing turn retried forever | Migration `0020_turn_limits`: `wa_turns_claim` fails a turn after `whatsapp.turn_max_attempts` (5) tries (`TOO_MANY_ATTEMPTS`, the last error kept) and unblocks the client; `hazem.failed_turns` (SECURITY DEFINER, ids and times only); a system alert `turn` for the managers (bell and e-mail, no client data) |
| 3. No limit per client | `whatsapp.turns_per_client_hour` (30): past it the client's messages wait and are grouped into the next turn (nothing dropped). The edge: `limit_req` 20/s, burst 200, then 429 (Meta sends again) |
| 7. Logs | Every long-running service: `json-file` 10 MB × 5. Chatwoot `LOG_LEVEL=warn` (at info it logged 51 request-parameter lines a week). n8n executions kept 7 days (`EXECUTIONS_DATA_MAX_AGE=168`, also changed in this PC's `.env`). The nightly log scan also reads Chatwoot, sidekiq, postgres and chatwoot-db (all clean today) |

Not done here (they wait for the owner or for TASK 14): 4 (what Staff may see), 5 (a `production` environment),
6 (an off-site backup copy), and the low findings 8–21.

## Results

- Images after the update (trivy, HIGH/CRITICAL with a fix): API 12 → **0**, cloudflared 7 → **0**, nginx 11 → 3
  (curl, brotli), n8n 66 → 32 and Chatwoot 40 → 17 (inside their own bundled libraries: the next releases).
- `backend-test` **509/509** (1 new), web **40/40** + `tsc`, `wa-db-tests` **64/64** (2 new), healthcheck **150/0**,
  `hazem-e2e` **131/131, all steps** on the new versions.
- Migration 0020 up → down → up on the live database.

## Found on the way

- `scripts/windows/hazem-e2e.ps1` run from a non-interactive Windows PowerShell turns the Arabic arguments into `?`
  (step 14 then fails: the name arrives as `???? ????`). Through bash (`scripts/linux/hazem-e2e.sh`) it passes. Not a
  fault of the system; run the e2e through bash, or fix the PowerShell wrapper's encoding later.
