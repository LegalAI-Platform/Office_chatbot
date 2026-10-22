# Handoff — session «sessioncloudchatamin» (2026-10-22)

For the next person (and their Claude) who continues this project from another machine. This
file replaces reading the whole chat: the raw transcript is **not** in the repository (it holds a
pasted API key and tool output; **this repository is public**). Everything below is free of
secrets, phone numbers and personal data.

**Read in this order:** `CLAUDE.md` → `docs/decisions/DECISIONS-v1.6.md` (the only source of
truth) → this file → `docs/sessions/2026-10-22-task05a.md` → `docs/sessions/2026-10-22-task04.md`
→ the prompt of the task you start (`TASK-0x-prompt.md`).

## 1. Where the project stands

`main` = commit `91f3778` (merge of PR #3). Done and merged:

| PR | Branch | What |
|---|---|---|
| #1 | `task04-hazem-brain` | TASK 04: Hazem's brain in .NET (state machines S-01..S-06, AI classification with approved texts only, the conversation flow) + DECISIONS v1.5 (B-25 topic) |
| #2 | `ai-classify-v3` | AI prompt `classify-v2` → `classify-v3`, hold-out set, service mistakes in `classify-eval` |
| #3 | `task05a-identify` | Approved texts live (migration 0007), TASK 05a (who is writing: lists B-14, existing clients B-04, roles B-14.4, source B-13, topic B-25, abuse S-01, real staff tasks S-05 with B-06 due times, `staff-events` B-12), DECISIONS v1.6 (B-09 urgent from the situation), `classify-v6`, then 8 review fixes |

Earlier tasks (TASK 00–03, v1.3/v1.4 decisions) are in `docs/sessions/2026-10-20-*.md`,
`2026-10-22-v1.4-*.md` and the cleaned chats in `docs/chat-history/`.

Checks on `main` (Windows, 2026-10-22): `backend-test` 266/266, `wa-tests` 62 + 9,
`hazem-e2e` 64/64, `healthcheck` 89/89 with one expected WARN (`openai-transcription`
credential not created yet). The Linux scripts were also run for TASK 05a (all PASS).

AI: `gpt-4.1-mini`, prompt `classify-v6`: intent 99.0% on 204 samples, 96.7% on the 60-sample
hold-out, urgent 100% (`docs/ai/classification.md`). Never tune prompts on the hold-out set.

## 2. What comes next

1. **TASK 07 — the panel** (recommended next, the owner chose «نستنى اللوحة» for alerts):
   show `hazem.staff_tasks` (type, staff, due, state) with the client's own words next to the
   topic (B-25, never in the calendar); call `POST /api/hazem/staff-events` when a staff member
   writes or presses «رجّع لحازم», then set `handoff.enabled = true` (and update the healthcheck
   that expects `false`); alerts (lawyer direct list, urgent, overdue tasks) and the B-06 promise
   texts become possible; unmute and list editing move from the CLI to the panel. Details:
   `docs/sessions/2026-10-22-task05a.md` §7.
2. **TASK 05b** — only after the owner finishes the Meta setup (`docs/meta-setup.md`).
3. A prompt is written per task first, the owner reviews it, then it is executed.

## 3. Waiting on the owner (not on the developer)

- Meta setup (manual, `docs/meta-setup.md`) — needed for 05b and anything live.
- The n8n credential `openai-transcription` (the healthcheck WARN).
- **Replace the OpenAI key** that was pasted in the chat on 2026-10-22 (it lives in the local
  `.env` as `HAZEM_OPENAI_API_KEY` on the first machine only).
- The office lists as CSV UTF-8 (`docs/runbooks/whatsapp.md` → «Importing the office's lists»)
  and staff names exactly as they should appear.
- `hazem.open_to_all` stays `false`; Hazem answers only `hazem.recipients` (test numbers).

## 4. How the owner works (keep it)

- Writes in Egyptian Arabic; answer in Egyptian Arabic, technical terms in English.
- Task by task: explain what you will do **before** doing it when asked («راجعه واشرحلي…»);
  list the decisions you need from the owner as short numbered questions with a recommendation.
- Commit / push / PR / merge **only when asked**. «راجع PR #n ولو سليم اعمله merge» means: run a
  real review; if there are real bugs, say so, fix them (when asked), rerun the four checks, then
  merge. Never merge a PR with known bugs.
- Never claim something works without running it; report numbers (tests passed, accuracy).
- Proposed client-facing texts stay inactive until the owner approves them; approved ones are
  activated in a migration with `updated_by = 'owner approval <date>'`.
- Owner decisions taken in chat that change behaviour go into a new DECISIONS version
  (v1.5 B-25, v1.6 B-09) and the owner says «اعتمدها» before it becomes the reference.
- At the end of every task: `docs/sessions/<date>-<task>.md`.

## 5. Decisions taken in chat (all already written in DECISIONS or the session files)

- TASK 04: consultation offer = the B-11 text; unclear → the offer; urgent = the urgent price +
  the offer + a task; texts reviewed mid-task.
- B-25 (v1.5): the AI records the topic (`matter_type` / `matter_subtype`); one optional question
  `ask_topic` inside the consultation offer, once per request, never on the urgent path; the
  topic is for the office only, Hazem never names it to the client.
- TASK 05a §0.1: a) handoff built but **off** (`handoff.enabled = false`) until the panel;
  b) colleagues / press / vendors get a neutral text + a task (once per request);
  c) abuse: one warning, then muted; unmute by `Office.Cli unmute`.
- The 5 TASK 05a texts were approved as proposed («اقترح إنت»), active in migration 0008.
- Texts that promise a contact time stay off until the panel (B-06).
- v1.6 (B-09): urgent also from the situation (arrest/detention, police at home or a warrant,
  a hearing / investigation / execution today or tomorrow), even without the word «مستعجل».
- An urgent message from a **listed existing client** goes to their staff member (neutral
  `existing_matter` text + `urgent_request` task), never the new-client urgent price.

## 6. Setting up a new machine

Follow `CLAUDE.md` → «First run on a new machine»: copy `.env.example` → `.env`, fill it with
`gen-secrets` (a new machine gets its **own** secrets; never copy the first machine's `.env`
through git or chat), `up`, `migrate up`, `n8n-import`, create the n8n owner at
http://127.0.0.1:5678, then `healthcheck`. `HAZEM_OPENAI_API_KEY` is typed into `.env` by the
owner only (empty = classification falls back to «unclear», healthcheck WARN). Keep
`N8N_ENCRYPTION_KEY` safe: n8n credentials cannot be read without it.

Each machine has its own database and containers, so two machines do not disturb each other —
but they share the GitHub branches: agree who works on which branch before pushing.

## 7. Practical notes from the first machine (Windows 11)

- Docker Desktop data was moved to `D:`; the repo is at `D:\Neno\law-office-whatsapp`.
- `gh` (GitHub CLI) and Python are **not** installed: PRs were opened and merged through the
  browser (GitHub account `ahmedamin778`, a collaborator on `LegalAI-Platform/Office_chatbot`).
- Scripts: `powershell -ExecutionPolicy Bypass -File scripts\windows\<name>.ps1`; in Git Bash
  use `MSYS_NO_PATHCONV=1` for container paths. `backend-test` uses the SDK container when no
  local .NET 10 SDK exists.
- Some files are CRLF: shell `sed`/`perl` edits broke on them; the editor tool worked.
- `migrate.ps1 up|down|status`; `migrate down` rolls back the last migration only.
- psql inside the postgres container: the superuser is `$POSTGRES_USER` (there is no `postgres`
  role).
- Counting test results with PowerShell `Select-String 'FAIL'` also matches «0 failed»
  (case-insensitive); use `-CaseSensitive` or the script's own summary line.
- Classification eval: copy the jsonl into the api container first (piping Arabic from Windows
  PowerShell breaks it) — `docs/ai/classification.md`.
- After any test run the healthcheck must pass: `whatsapp.dry_run = false`, base URLs back to
  Meta/OpenAI, no test credential left in n8n, `responder = hazem`, `handoff.enabled = false`.

## 8. Code map for TASK 05a (where to look)

- Flow: `backend/src/Office.Application/Responders/HazemResponder.cs` (per-turn `Flow`:
  abuse → topic/role → lawyer direct → name → B-14 type → existing client B-04 → answer → offer).
- Domain: `backend/src/Office.Domain/Clients/Clients.cs` (`Person`, `Conversation`, `Intake`),
  `Tasks/StaffTask.cs` (S-05), `Tasks/OfficeHours.cs` (B-06), `Common/PhoneNumbers.cs`.
- Lists / overdue: `backend/src/Office.Infrastructure/Lists/OfficeLists.cs`; CLI commands in
  `backend/src/Office.Cli/Program.cs` (`import-lists`, `tasks-overdue`, `unmute`,
  `classify-eval`, `send-test`).
- AI: `backend/src/Office.Infrastructure/Ai/OpenAiIntentClassifier.cs`, prompts in
  `Ai/prompts/classify-v1..v6.md` (a change = a new file + a new `PromptVersion`).
- API: `backend/src/Office.Api/Hazem/HazemEndpoints.cs` (`turns`, `send-results`,
  `staff-events`); contract in `docs/hazem-internal-api.md`.
- Database: `database/migrations/0005`–`0008`; e2e: `infra/tools/bin/hazem-e2e`;
  healthcheck: `infra/tools/bin/healthcheck`.
