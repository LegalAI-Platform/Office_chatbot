# Session report — 2026-10-27 — Hazem on Meta's test number, booking and voice switched on

A Claude Code session in the desktop app, driven step by step by the owner («خطوة خطوة»). It moved Hazem
from the WAHA test channel (TASK 02b) to **Meta's official WhatsApp Cloud API test number**. It then
switched on the booking with a Google Calendar and voice transcription. Two bugs found on the way were fixed
in PR #22 and PR #23. No DECISIONS change: everything runs as v1.19 describes.

Personal numbers and e-mail addresses are masked as in `docs/chat-history/README.md`. No secret is in this file.

## 1. Meta (the test number)

| Step | Result |
|---|---|
| Meta app | **Hazem Office Bot**, in the business portfolio **Hazem Office** (unverified), on the owner's own Facebook account. The first portfolio could not create a WhatsApp Business Account (phone code locked), so it was left |
| Test number | Meta's free `+1 555 …` number; the owner's phone (`2011******71`) is the one verified recipient |
| `app.settings` | `whatsapp.app_id`, `whatsapp.waba_id`, `whatsapp.phone_number_id`, `whatsapp.graph_api_version` = `v26.0`; `whatsapp.provider` `waha` → **`meta`** (WAHA keeps running but is not heard) |
| System user | `office-n8n` (Admin) with the app and the test WABA, both Full control; token **Never** expires, `whatsapp_business_messaging` + `whatsapp_business_management` (`business_management` was not offered and is not needed) |
| n8n credentials | `meta-whatsapp-trigger`, `meta-whatsapp-token`, `meta-app-token`, made by the owner in n8n (`docs/meta-setup.md` part 3) |
| Webhook | `tunnel-refresh` run by the owner (the agent's auto mode refuses to expose n8n): `OK Meta webhook -> https://…trycloudflare.com/webhook/…` |
| End to end | Two messages became one turn, and Hazem's reply was delivered and **read** |

**Problems on the way:**
- The token was pasted without `Bearer `, so Meta answered `(#200) insufficient_scope`. The agent added the prefix inside n8n
  (export → fix → import), and the token was never printed.
- **Bug, fixed in PR #22:** a new test WABA comes subscribed to Meta's own «WA DevX Webhook Events 1P App».
  `META-WA-SUBSCRIPTION-CHECK` subscribed our app only when the list was empty, so Meta delivered nothing. It now subscribes
  whenever `whatsapp.app_id` is missing from the list.
- `n8n-import` unpublishes `META-WA-INBOUND` (as designed). After every import, `tunnel-refresh` has to run again.
- A message from a second phone (`2010******77`) was stored and processed, but Hazem did not answer: it is not in
  `whatsapp.test_recipients` / `hazem.recipients`, which is correct.

## 2. «Forget this number»

The owner asked twice for Hazem to forget the test phone and greet it as new. Both times, in one transaction after a
`pg_dump` to `exports/manual/office-before-forget-…dump` (git-ignored), every row for that `wa_id` was deleted:
- `hazem.persons`, `conversations`, `intakes`, `staff_tasks`, `turns`, `turn_replies`, `chatwoot_turns`, `chatwoot_threads`
  (with appointments / payments / orders / ʿurfi cases, none existed);
- `app.wa_inbound_messages`, `wa_outbound_messages`, `wa_message_statuses`, `wa_turns`, `wa_contact_windows`, the matching
  `wa_webhook_events`.

Redis held nothing for it. Chatwoot keeps its own copy of the old conversation (its own database).
**There is no command for this yet:** the SQL lives only in this session. A `forget-contact` CLI command (test environment
only) would be the clean way.

## 3. The first conversation, checked against the rules

| Client | Hazem | Rule |
|---|---|---|
| السلام عليكم | the B-01 welcome asking for the four-part name | ✅ B-01 |
| a two-part name | «معلش، ممكن اسم حضرتك رباعي؟» (once) | ✅ B-02 |
| the four-part name | the consultation offer (500 / 1,000) + the optional topic question | ✅ B-01.3, B-25 |
| «قضية طلاق» | «تمام، وصلت ✅», topic `personal_status` / `divorce` | ✅ B-25 |
| «في مشكلة كبيرة…» | «تمام، وصلت ✅» again | ✅ by B-25's letter, but cold for a worried client |

**Gap found:** with `booking.enabled = false`, the offer has **no buttons**, and no task reaches the office. The client
is left with nothing to do after «تمام، وصلت ✅». This led to switching the booking on (part 4). With the booking off for
any reason, the offer probably should create a task for the office; this is not decided yet.

## 4. Booking with the Google Calendar

| Step | Result |
|---|---|
| Google Cloud | project `hazem`, **Google Calendar API** enabled, service account `hazem-calendar@…iam.gserviceaccount.com` (no role) |
| Key | JSON key as one line of base64 in `GOOGLE_SERVICE_ACCOUNT_JSON`, written by a PowerShell one-liner that also deleted the file. **The first key was attached to the chat by mistake**: the owner was told to delete it in Google Cloud and make a new one (done for the new key; deleting the old one is the owner's to confirm) |
| Calendar | the owner's own Gmail calendar for testing, shared with the service account («Make changes and see all event details»); `booking.calendar_id` saved from the panel |
| Check | `calendar-check --write`: `ok: true`, `write_test: created_and_deleted` |
| Office details | **test values**: InstaPay `test-office@instapay`, wallet `01000000000` (فودافون كاش), a test address and a Cairo map link. They were written straight to `hazem.settings` (`updated_by = claude:test-data`), so the panel's «تراجع» does not cover them. **Replace them before any real client books** (healthcheck WARNs) |
| Switch | `booking.enabled` = `true` (`updated_by = claude:owner-asked`) |
| Hours | as shipped: every day 10:00–24:00 |
| healthcheck | **153 passed, 0 failed**, with 2 warnings: the test payment details, and (then) no `openai-transcription` |

## 5. Voice notes

`openai-transcription` (Header Auth) was made **from the existing `HAZEM_OPENAI_API_KEY`**, at the owner's choice
(«استخدم الموجود»). T-08 prefers a separate key. One key is fine for testing; split them before production if the cost or
the risk should be seen apart. It was made inside the n8n container from `.env` through a pipe, so the key was never printed.
Then `n8n-import` (the warning is gone) and `tunnel-refresh`.

## 6. Bug: the Arabic sender name in the office's e-mails (PR #23)

The office's e-mail «عميل عايز يكلم حد» came with a garbled sender name. `scripts/windows/_common.ps1` read `.env` with a bare
`Get-Content`. Windows PowerShell 5.1 reads a BOM-less file in the ANSI code page, and `Set-EnvValue` wrote it back as UTF-8.
So every `tunnel-refresh` garbled `SMTP_FROM_NAME` one layer deeper, from 60 to 3,827 characters in one day. It was the only
non-ASCII value in `.env`. Fixed with `-Encoding UTF8` in `Get-EnvValue`, `Set-EnvValue` and `Assert-NoPlaceholders`. Two
rewrites of a copy of the real `.env` left its hash unchanged. The name was restored to «مكتب سعد فتحي سعد» (the original was
lost long before: four words of 4/3/4/3 letters, the same as in the e-mail body). The API and Chatwoot were restarted.

## 7. Also seen

- `scripts/windows/healthcheck.ps1`, `backup.ps1` and the first `tunnel-refresh.ps1` run **stop on Docker's «Image
  office-tools:local Building» line on stderr** (PowerShell 5.1 `NativeCommandError`), although nothing failed.
  `docker compose run --rm tools healthcheck` from bash works. Not fixed.
- Postgres came up in crash recovery after the disk filled (WAL replay, about 90 s, no data lost).
- The session first worked on an old stale copy of the project on `C:` by mistake. It was reverted, and the agent's memory
  now says the project is `K:\law-office-whatsapp` only.

## 8. Open, for the owner

1. **Delete the leaked service-account key** (`0a1ef0ef…`) in Google Cloud → `hazem-calendar` → Keys, if not done, and its file from Downloads.
2. **Regenerate Meta's temporary token** (API Setup → Generate new token): it appeared in a screenshot earlier.
3. Replace the **test office details** with the real ones before real clients.
4. Production later: a verified, office-owned portfolio, the office's own number (DECISIONS part 5), the lawyer's real
   calendar instead of the owner's.
5. Decide whether an offer without booking should make an office task (part 3).

## 9. PRs

- #22 `fix-meta-subscription-check`: META-WA-SUBSCRIPTION-CHECK for pre-subscribed test WABAs (merged).
- #23 `fix-env-utf8`: read `.env` as UTF-8 in the Windows scripts (merged).
- This report and `web/.gitignore` (`*.tsbuildinfo`): the PR after #23.
