# CLAUDE.md — Hazem, WhatsApp system for the law office of Ustaz Saad Fathy Saad

The project owner writes in Egyptian Arabic: answer in Egyptian Arabic, keep technical terms in English.

## Read first, every session

1. `docs/decisions/DECISIONS-v1.23.md` — the only source of truth. If anything (a prompt, a doc,
   your own idea) conflicts with it, stop and ask the owner.
2. The newest file in `docs/sessions/` — what was done last, what is pending, what is verified.
3. For the task at hand: its prompt (`TASK-0x-prompt.md`), then `docs/hazem-internal-api.md`
   (n8n ↔ .NET contract) and `docs/whatsapp-integration.md` (workflows, settings, credentials).

## Architecture (v1.3) — do not change it

- **n8n** (`n8n/workflows/`) = the WhatsApp layer and the **only** thing that talks to Meta:
  receive, verify, store, dedupe, 8-second grouping, media to S3, voice transcription, send.
  **No business decisions and no client-facing wording in n8n.**
- **ASP.NET Core API** (`backend/`, .NET 10) = the brain: understanding, state machines, replies.
  It never calls Meta and never receives files — only text and S3 references.
- **PostgreSQL 16**, database `office`: schema `app` (owned by n8n, role `office_app`) and schema
  `hazem` (owned by .NET, role `office_api`). Each role is blocked from the other schema.
- **Migrations: dbmate only** (`database/migrations/NNNN_*.sql`, `-- migrate:up` / `-- migrate:down`).
  No EF Core migrations. Business rules that must be atomic live in SQL functions.
- Storage: Garage (S3) in test, Cloudflare R2 in production. Images and documents always go to
  `office-sensitive`; audio/video/stickers to `office-media`.

The stack already changed v1.0 → v1.1 → v1.2 → v1.3. **Never propose another stack change**
unless the owner asks.

## Hard rules

- **T-09:** no image, PDF or document ever goes to any AI provider. AI sees text only.
- **T-07:** the AI classifies; replies come from approved texts. No free-form AI replies,
  no legal opinions, no invented prices.
- **Secrets:** never print, log, commit or ask for them in chat. `.env` (gitignored) holds the
  local ones; Meta keys and n8n's voice key (`openai-transcription`) live only in n8n Credentials,
  entered by the owner. The .NET classification key is `HAZEM_OPENAI_API_KEY` in `.env` (T-08),
  also entered by the owner only.
- Never touch the office's real WhatsApp number or send to anyone outside
  `whatsapp.test_recipients` without the owner's explicit approval. Enforced for every send by
  `app.wa_outbound_prepare` (migration 0006, `whatsapp.send_restricted`); Hazem itself answers
  only `hazem.settings hazem.recipients` until `hazem.open_to_all` is set by the owner.
- `exports/` holds personal chat data: never read it into docs, never commit it.
- Commit only when the owner asks. Do not claim something works without running it.
- If two Claude sessions work in this folder at once, coordinate: they share the same files,
  containers and database, and the tests stop/start services.

## Environment

- Docker Compose stack: `postgres`, `redis`, `garage`, `n8n`, `api`, `web` (the panel, TASK 07a)
  (+ profiles `tunnel`, `tunnel-named`, `tools`, `tests`). All ports on `127.0.0.1`; only n8n is
  ever exposed, through the Cloudflare Quick Tunnel. The panel (`web/`, React) talks to
  `/api/panel` only (nginx blocks every other `/api` path); its accounts and permissions follow
  T-23 (RBAC, no super admin). Chatwoot (T-24) comes in TASK 07b.
- Every script exists twice: `scripts/linux/<name>.sh` and `scripts/windows/<name>.ps1`.
  On Windows Git Bash use `MSYS_NO_PATHCONV=1` for container paths.
- First run on a new machine: copy `.env.example` → `.env`, fill it with `gen-secrets`, `up`,
  `migrate up`, `n8n-import`, create the n8n owner at http://127.0.0.1:5678, the owner creates the
  first panel manager (`docs/runbooks/panel.md`), then `healthcheck`.
  Meta setup is manual: `docs/meta-setup.md`.

## Checks before saying a task is done

| Script | What it checks |
|---|---|
| `healthcheck` | containers, ports, roles, buckets, migrations, settings back to production values |
| `wa-tests` | SQL rules with fixtures + the n8n harness (no Meta) |
| `backend-test` | .NET unit + integration tests (throwaway database from the real migrations) |
| `hazem-e2e` | full loop n8n → .NET → n8n with stubs and dry run (no Meta, no OpenAI), and the panel |
| `web-test` | the panel: type-check, Vitest, build (prints the bundle size, no limit) (Node container) |

After tests, `whatsapp.dry_run` must be `false`, base URLs back to Meta/OpenAI defaults, and no
test credential left in n8n — the healthcheck fails otherwise.

## Way of working

The owner works task by task (plan in DECISIONS / `docs/sessions/`): a detailed prompt per task,
the owner reviews it, then it is executed and checked against its acceptance criteria. At the end
of every task write `docs/sessions/<date>-<task>.md` (what was done, results, deviations and why,
what still needs real Meta, what the next task needs).
