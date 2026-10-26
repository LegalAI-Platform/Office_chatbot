# Threat model — what we protect, from whom, and where they could get in

One page, kept with the code. Reviewed on 2026-10-26 (after TASK 13). Update it when a task adds an entry point, a
new kind of data, or a new outside service. The findings of each review go to `docs/security/` next to it.

## 1. What we protect (assets)

| Asset | Where it lives | Why it matters |
|---|---|---|
| Clients' phone numbers, names, cases, appointments, payments | Postgres `office` (`hazem`, `app` schemas) | Personal and legal data; a leak harms clients and the office |
| Clients' WhatsApp messages (text, voice notes) | Postgres `office`, n8n's executions, Chatwoot's database | Same, and lawyer–client privilege |
| Client files: IDs, receipts, contracts, delivered documents | Garage `office-sensitive`, `office-media` | The most sensitive data in the system |
| Backups of all the above | Garage `office-backups`, exports | A copy of everything; encrypted with `BACKUP_ENCRYPTION_KEY` |
| Meta's WhatsApp token | n8n credential (encrypted with `N8N_ENCRYPTION_KEY`) | Whoever has it can message any client as the office |
| The other secrets (`.env`) | The office PC only | Keys to every component (`docs/runbooks/secrets.md`) |
| Staff accounts and sessions | Postgres `office` (`app.panel_*`), the session cookie | Each one opens the panel with its role's permissions |
| The bot's behaviour | The approved texts, the classifier, the booking rules | A manipulated bot could tell a client something false in the office's name |

## 2. Where someone could get in (entry points)

| Entry point | Reachable from | Who is supposed to use it |
|---|---|---|
| Meta's webhook (`/webhook/<id>/webhook` via the tunnel → `edge` → n8n) | The internet | Meta only |
| Message content inside that webhook (text, media, buttons) | Any WhatsApp user | Clients; **always treated as hostile input** |
| The panel (`web` → `/api/panel/*`) | `127.0.0.1:8080` on the office PC (TASK 14 puts it on the internet) | Staff, by role |
| The internal API (`/api/hazem/*`) and n8n's internal send webhook | The Docker network only | n8n ↔ the API, with bearer tokens |
| n8n's and Chatwoot's own UIs, Garage's admin | The office PC (localhost) | The owner / staff |
| Outbound fetches (media from Meta, OpenAI, Google Calendar, Gmail SMTP) | The API / n8n going out | Fixed outside services |
| The office PC itself, Docker Desktop, `.env`, the backups' export folder | Physical / Windows access | The owner |

## 3. Who could attack, and what they would try

| Attacker | Likely goal | Main defences today |
|---|---|---|
| Anyone on the internet | Reach n8n / the API / the panel; forge Meta events; flood the webhook | Only the webhook path passes the edge; everything else is localhost; the tunnel has its own network |
| A client (or someone with a client's number) | See another client's data; make the bot say or do something; send a harmful file; spam | Every reply is an approved text; files go to the sensitive bucket and are never executed; per-number state |
| Prompt injection through a message | Make the AI classifier leak data or change the bot's actions | The AI only picks a category; it never writes the reply and never sees other clients' data (to verify each review) |
| A staff member with an account | See or change what their role should not; take over a manager's account | Roles and permissions on every endpoint; manager approval; 2FA available; audit columns |
| Someone with the office PC or a copy of the disk | Read everything | Windows account; backups encrypted; `.env` not in git (no disk encryption yet — owner) |
| A compromised dependency or image | Run code inside a container | Pinned versions; non-root, read-only containers where possible; scans each review |

## 4. Trust boundaries

1. Internet → `edge` (the only open door).
2. `edge` → n8n (only Meta's path).
3. n8n ↔ API (bearer tokens; the Docker network).
4. Browser → panel → API (cookie session, roles).
5. Services → Postgres / Garage (one role or key per service, least privilege).
6. Anything → outside services (fixed hosts; private addresses refused unless `SAFE_FETCH_ALLOW_PRIVATE_NETWORK`).

## 5. How it is checked

- Every night: `healthcheck` (the controls above still in place), `log-scan` (no tokens or full numbers in logs).
- Each review: dependency scans (`npm audit`, `dotnet list package --vulnerable`), secrets in git (`gitleaks`), code
  rules (`semgrep`), images (`trivy`), a manual review against this page, and a live scan of the local system (ZAP).
- Before real clients' data: an outside penetration test.
