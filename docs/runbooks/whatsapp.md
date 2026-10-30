# Runbook — WhatsApp (n8n, test environment)

Windows form shown (`scripts\windows\*.ps1`); Linux has the same names in `scripts/linux/*.sh`.

## Start / end of a test session

1. Docker Desktop running → `up` → `tunnel-refresh` (7 steps: owner check, credentials/settings,
   tunnel, delete old subscription, update `WEBHOOK_URL`, publish `META-WA-INBOUND` + restart n8n,
   verify the callback URL at Meta).
2. End of session: `tunnel-down`.

## The tunnel URL changed (restart of the tunnel, Docker or the PC)

Symptom: messages to the test number never reach `app.wa_webhook_events`.
1. Run `tunnel-refresh`. It deletes the old subscription, points `WEBHOOK_URL` at the new URL,
   republishes `META-WA-INBOUND` (the trigger registers the new URL on start-up) and checks Meta.
   Since 2026-10-27, step 3 waits until the new link answers (`/healthz` twice, up to about two
   minutes), because Meta checks the callback the moment the trigger registers it.
2. If step 7 fails (`Meta callback is ''`): Meta refused the registration, usually because the link did
   not answer yet. n8n records it in `workflow_publication_outbox` («"WhatsApp Trigger": Bad request -
   please check your parameters») and **does not retry**, not even after a restart or a re-publish of
   the same version. Run `n8n-import` (new versions), then `tunnel-refresh` again. Otherwise: open
   n8n → `META-WA-INBOUND` → check it is published and has no errors; run `META-WA-SUBSCRIPTION-CHECK`
   by hand and read `callback_url`.

## After a `reset` (all data wiped)

1. `up` → `migrate up` → `n8n-import`.
2. Create the n8n owner at http://127.0.0.1:5678 **before** any tunnel.
3. `n8n-import` creates `office-postgres-app`, `hazem-api` and `hazem-internal-send` from `.env`
   by itself; its warning lists the Meta credentials that are still missing.
4. Meta credentials again (docs/meta-setup.md part 3) and the `whatsapp.*` settings.

## The .NET API is down (DECISIONS v1.3, T-20)

Symptom: client messages stay `pending` in `app.wa_inbound_messages` with
`last_error = {"code": "API_UNAVAILABLE"}`; the healthcheck says a text message is waiting
for more than 10 minutes.
1. `status` / `logs api`: is the `api` container running and healthy?
2. `docker compose run --rm tools sh -c 'curl -s http://api:8080/health/ready'` must print
   `Healthy`. `Unhealthy` usually means migration 0003 is missing: `migrate up`.
3. Start it (`up`). Nothing is lost: WA-FORWARD-TURN picks the waiting messages up within a minute.

## n8n and the API refuse each other's token

Symptom: `app.workflow_errors` shows "The API refused X-Hazem-Token", or the API's sends come
back with `N8N_HTTP_403`.
1. `HAZEM_API_TOKEN` and `N8N_INTERNAL_SEND_TOKEN` in `.env` must be set and different.
2. Run `n8n-import` (rewrites the credentials `hazem-api` and `hazem-internal-send` from `.env`),
   then `docker compose up -d api` (re-reads `.env`).
3. To change a token: new value in `.env`, then step 2. Both sides always read it from `.env`.

## A turn stuck in processing (TASK 03)

Symptom: the healthcheck says a turn is in `processing` for more than 10 minutes, or a client
waits for an answer. Normally nothing is needed: after `whatsapp.turn_stale_seconds` (300) the
every-minute WA-FORWARD-TURN takes it again with the **same** `turn_id`, and nothing is sent
twice (the API returns the same actions, n8n's idempotency keys stop a second send).
1. Look at it: `SELECT turn_id, status, attempts, claimed_at, last_error FROM app.wa_turns WHERE status IN ('processing','pending') ORDER BY claimed_at;`
2. Is n8n running and is WA-FORWARD-TURN published? (`n8n-import` publishes it and restarts n8n.)
3. Many attempts and `last_error.code = API_UNAVAILABLE`: see "The .NET API is down".
4. Never delete the turn by hand: the client's later messages wait behind it on purpose.

## A media file failed (TASK 03)

`SELECT w.status, w.last_error, w.attempts FROM app.wa_media w JOIN app.wa_inbound_messages m ON m.id = w.inbound_message_id WHERE m.wamid = '<wamid>';`

| `last_error.code` | Meaning | What to do |
|---|---|---|
| `TIMEOUT` (stage download) | not downloaded within `group_max_seconds`; the turn went without it | check `meta-whatsapp-token` and n8n's executions of WA-MEDIA-ONE |
| `META_MEDIA_INFO` / `DOWNLOAD_FAILED` | Meta refused (http_status 401/190 = token, 404 = expired media) | fix the token; an expired media cannot be fetched again: ask the client to resend |
| `SHA256_MISMATCH` | the file differs from what Meta announced | nothing stored; ask the client to resend |
| `S3_UPLOAD_FAILED` | Garage refused | `healthcheck` (n8n key write checks), `logs garage` |
| `TOO_MANY_ATTEMPTS` | three claims failed | as above |
| `NO_GRAPH_SETTINGS` | `whatsapp.graph_api_version` is empty | set it in `app.settings` |
| `TOO_LARGE` / `MIME_NOT_ALLOWED` | over `media_max_mb` / type not allowed | by design; the API decides what to tell the client (TASK 04) |

## Voice transcription failed, or the OpenAI key was revoked (TASK 03)

Symptom: `wa_media.transcript_status = 'failed'` with `last_error.code = TRANSCRIPTION_FAILED`
(`http_status` 401 = key missing or revoked, 429 = quota) or `TIMEOUT` (stage transcription).
The turn still reaches the API, without text. The original audio stays in `office-media`.
1. 401: create a new key at OpenAI, then n8n → Credentials → `openai-transcription` → `Bearer <new key>`
   → Save (owner only, docs/meta-setup.md 3.4). If the credential did not exist before, run `n8n-import` once.
2. Model problems: `ai.transcription_model` in `app.settings`.
3. Notes that already failed are not transcribed again automatically.

## Test stub vs real services (TASK 03)

`hazem-e2e` points `whatsapp.graph_base_url` and `ai.openai_base_url` at `meta-stub` and
restores the original values at the end, even when it fails. If a test was killed half-way:
1. `docker compose run --rm tools hazem-e2e cleanup` (restores every setting it backed up as
   `e2e.backup.*`, switches dry run and the echo off, deletes the dummy rows and files).
2. `docker compose run --rm tools hazem-e2e remove-test-credentials`, then `n8n-import`.
3. `docker compose --profile tests rm -sf meta-stub`.
4. `healthcheck`: real URLs, `turn_stale_seconds = 300`, dry run off, no test credential, stub
   stopped, `responder = hazem`, `ai.stub_enabled = false`.
Never point the two URLs at the stub by hand outside a test.

## Hazem's AI is down, or HAZEM_OPENAI_API_KEY was revoked (TASK 04)

Nothing stops: every text turn is classified `other` and gets the "unclear" answer (today the
consultation offer, until the owner approves the "unclear" text). Look at the reason:

```sql
SELECT fallback_reason, count(*) FROM hazem.classifications
 WHERE created_at > now() - interval '1 hour' GROUP BY 1;   -- no_key / timeout / error / invalid_output / low_confidence
```

- `no_key`: `HAZEM_OPENAI_API_KEY` is empty in `.env`.
- `error`: OpenAI refuses (a revoked key gives 401): put a new key in `.env`, then
  `docker compose up -d api`. Never paste the key in a chat or a log.
- `timeout` / `error` for a while: OpenAI is down; the answers keep going out.

## Importing the office's lists (TASK 05a, B-14)

Three lists: `existing_client` (name, phone, staff in charge), `lawyer_direct`, `blocked`.

1. In Excel: columns **name, phone, staff** (staff only for clients) with that header row.
   File → Save As → **CSV UTF-8 (Comma delimited)**. Phones may be written any way
   (`010…`, `+20…`, Arabic digits, spaces, dashes).
2. Copy it into the api container and import (`--replace` swaps the whole list in one go, and
   only when **every** line is valid: with one refused line nothing changes and the old list
   stays). A cell written on two lines in Excel (Alt+Enter) is read as one cell:

```powershell
docker compose cp clients.csv api:/tmp/clients.csv
docker compose exec api dotnet /app/cli/Office.Cli.dll import-lists --kind existing_client --file /tmp/clients.csv --replace
```

3. The output says how many lines were imported and lists refused lines by **line number** (the
   number itself is never printed). Fix them in Excel and import again. Delete the CSV afterwards:
   it holds personal data.

A listed client whose name has four parts is never asked for the name (B-01.5). A blocked number
gets no answer at all and its conversation is muted.

## Insults and spam (S-01) — unmuting a client

Hazem warns once (text `abuse_warning`, after the owner approves it) and records a `complaint`
task; the next time the conversation is muted for good. Only the manager brings it back:

```powershell
docker compose exec api dotnet /app/cli/Office.Cli.dll unmute --to 2010XXXXXXXX
```

## Overdue staff tasks (S-05)

Every task has a due time from B-06. `tasks-overdue` marks the late ones (run it by hand, or every
few minutes later). Alerts to the lawyer and apologies to the client come with the panel (TASK 07).

```powershell
docker compose exec api dotnet /app/cli/Office.Cli.dll tasks-overdue
```

## Changing the classification model (TASK 04, T-08)

1. Run `classify-eval` with the candidate (`--model …`, [docs/ai/classification.md](../ai/classification.md)).
2. Put the chosen name in `OPENAI_MODEL` in `.env`, then `docker compose up -d api`.
3. Every new row of `hazem.classifications` shows the model it used.

## Approving or changing an approved text (TASK 04)

Hazem sends only active rows of `hazem.approved_texts`. A text is never edited in place: a
change is a new version, so every reply keeps pointing at the exact words it sent
(`hazem.turn_replies`). Only with the owner's written approval of the exact words:

```sql
-- approve a proposed text as it is (docs/approved-texts-proposal.md)
UPDATE hazem.approved_texts SET active = true, updated_by = 'owner approval <date>'
 WHERE key = 'unclear' AND version = 1;

-- change a text: deactivate the old version, add the new one (same transaction)
BEGIN;
UPDATE hazem.approved_texts SET active = false WHERE key = 'unclear' AND active;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
VALUES ('unclear', 2, '…النص الجديد…', 300, true, 'owner <date>', 'owner approval <date>');
COMMIT;
```

`max_chars` is 300 for ordinary messages and 600 for payment, address and document messages
(B-21). A text with a part-5 placeholder such as `{{OFFICE_ADDRESS}}` or a `[…]` slot is never
sent (the healthcheck fails while one is active). Then run `healthcheck`.

## Forget one number (test only)

To test a conversation from the start with the same phone («انسى الرقم ده»).

**From the panel** (the manager and the lawyer): الإعدادات ← **أدوات التجربة**, or Ctrl K «حازم ينسى رقم تجربة». It lists
Hazem's test numbers (`hazem.recipients`, the end of each number only) with «انسى الرقم ده» and «انسى كل الأرقام»,
asks first, and does steps 2 and 3 below (no backup first: the nightly one is there; Redis is left, n8n's keys there
expire in seconds). Only in the test environment (the API answers 404 anywhere else) and only on those numbers; each
use is written to the panel's audit (`testing.forget`).

**From a terminal** (where the stack runs, on the server too):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\windows\forget-number.ps1 01012345678
```

(`scripts/linux/forget-number.sh 01012345678` on Linux; `+20 10…` and `2010…` work too.) It refuses unless
`ENVIRONMENT=test`, then:
1. backs up `office` and Chatwoot to `exports/manual/` (git-ignored), and stops if either fails;
2. `Office.Cli forget-number`: `hazem.forget_wa_id` (migration 0025) deletes every row of the number in one
   transaction (person, conversation, intakes, tasks, turns, bookings, payments and receipts, service orders, Urfi
   cases, Chatwoot links, messages, the 24-hour window, raw events), then its bookings' **Google Calendar events**
   are deleted at Google (any that fail are printed with their time, to delete by hand);
3. Chatwoot deletes the contact and its conversations;
4. Redis keys with the number are deleted.

The number stays in `whatsapp.test_recipients` / `hazem.recipients`. Files in storage (a receipt image) are not
deleted; nothing points at them any more.

## End-to-end test without Meta

`hazem-e2e`: message → n8n → API → reply (dry run) → result back; same turn twice; API down and
back; API → n8n send; wrong tokens; TASK 03: two texts and a photo grouped into one turn, PDF,
voice note (stub transcript), too large, wrong sha256, forbidden type, a turn stuck while n8n
restarts; TASK 04 (responder `hazem`, classification by the stub): booking request → welcome →
name → offer, legal question → B-20 + offer, a photo never reaches the AI, a phone number is
masked, the AI down still gets an answer. Steps 1-7 run with `responder = echo`.
Uses dummy numbers 20100000006x and `meta-stub`; restores every setting, removes its
temporary credentials and checks n8n's credentials are exactly as before.
`backend-test` runs the .NET unit and integration tests; `wa-tests` the SQL rules and the harness.

## Access token expired or revoked (Meta error 190)

Symptom: `app.workflow_errors` / Telegram show `Meta token invalid`; sends fail with `TOKEN_INVALID`.
1. Business Settings → System users → generate a new token (docs/meta-setup.md part 2.3).
2. n8n → Credentials → `meta-whatsapp-token` → `Bearer <new token>` → Save.
3. Run `META-WA-SUBSCRIPTION-CHECK` to confirm.

## Meta is not sending events

1. `META-WA-SUBSCRIPTION-CHECK`: callback URL correct? WABA subscribed to the app?
2. Is the sender in the **test number's** recipient list? Only listed numbers can write to it.
3. Executions of `META-WA-INBOUND` absent → signature or App Secret problem: check
   `meta-whatsapp-trigger` (Client ID = App ID, Client Secret = App Secret).

## Template rejected or re-categorised

1. Run `META-WA-TEMPLATES-SYNC`; read `app.wa_templates` (`status`, `category`, `rejection_reason`).
2. Fix the text in `n8n/templates/whatsapp-templates.json` under a **new name**, regenerate
   `META-WA-TEMPLATES-SUBMIT`, import, run it.

## Rate limits (RATE_LIMIT)

One automatic retry after 3 s for HTTP 429. Persistent limits on the test number are normal;
real limits rise after Business Verification.

## A send timed out (NETWORK_OR_UNKNOWN)

Not retried on purpose: Meta may already have delivered it. Check the customer's chat (or
`app.wa_message_statuses` for a later status) before sending again with a **new** idempotency key.

## Moving the office's real number (plan only — needs the project owner's explicit go-ahead)

Only after Hazem is built and tested on the test number, and after Business Verification.
1. Back up the office WhatsApp chats on the phone (Settings → Chats → Chat backup).
2. Choose **Coexistence** (WhatsApp Business app keeps working; check availability for this
   number/country at that time) or **Migration** (API only; old chats do not move).
3. Add the number to the production WABA, verify it, get «مكتب الأستاذ سعد فتحي سعد» approved as display name.
4. Production needs a stable HTTPS domain (T-13) and its own Meta App / trigger.
5. Update `whatsapp.phone_number_id` / `whatsapp.waba_id`, resubmit templates on the production WABA,
   test with one internal number before announcing.
