# TASK 02b — قناة WAHA للتجربة لحد موافقة Meta (DECISIONS v1.19)

Branch `task02b-waha-test-channel` from `main` (`f7130af`). Read DECISIONS v1.18 (T-03 n8n is the only part that talks
to WhatsApp, M-01 / M-02 / M-03, the test recipients of migration 0006), `docs/whatsapp-integration.md`, the TASK 02 / 03
reports, and the n8n workflows `META-WA-INBOUND`, `META-WA-PROCESS-EVENT`, `META-WA-SEND-MESSAGE`, `META-WA-SEND-TEMPLATE`,
`WA-SEND-ACTION`, `WA-MEDIA-ONE`.

## Owner's decisions (the chat, «موافق»; the prompt approved: «موافق على ملف تاسك 02b، ابدأ التنفيذ»)

| # | Decision |
|---|---|
| 1 | WAHA (WhatsApp HTTP API, the free edition) is a **second, temporary channel for testing** until Meta approves the account. **Nothing of the Meta flow is removed or changed in behaviour**: one setting chooses the channel, and the default stays Meta |
| 2 | **A spare number only**, never the office's number. WhatsApp can ban a number that WAHA runs (it is not an official API); a spare number makes that harmless |
| 3 | The engine is **NOWEB** (no browser inside, little memory) |
| 4 | WhatsApp shows no buttons or lists through WAHA, so they go as **a numbered list** («1) هحضر ✅ / 2) تغيير أو إلغاء»), and a reply «1» comes back to the API **as the press of that button**. Hazem and the API never know which channel it was |

## The design

The adapter lives **in n8n only**. The API, Hazem, the texts, the booking, the payments, the Urfi path and the
reminders do not change.

```
WAHA ──webhook──▶ WAHA-INBOUND (n8n) ──Meta's event shape──▶ META-WA-PROCESS-EVENT ─▶ (the same turns, media, API)
API ──▶ WA-INTERNAL-SEND ─▶ WA-SEND-ACTION ─▶ META-WA-SEND-MESSAGE ──provider = meta──▶ graph.facebook.com (as today)
                                                                   └─provider = waha──▶ WAHA (sendText / sendImage / sendFile)
```

- **The rules stay Meta's rules** on both channels: the test recipients (0006), the 24-hour window, the template approval,
  idempotency, the dry run. Only the transport changes. So the test behaves like production: a reminder outside the
  24 hours still becomes a task until Meta approves the templates (WAHA could send it, but production could not).

## What to build

1. **Migration 0021** (`database/migrations/0021_waha_test_channel.sql`):
   - Settings: `whatsapp.provider` = `meta` (`meta` | `waha`), `waha.base_url` = `http://waha:3000`, `waha.session` =
     `default`. The healthcheck WARNs while it is `waha`.
   - `app.waha_menus`: the last numbered list sent to a number (`wa_id`, the outbound id, the choices as `[{n, id, title}]`,
     `sent_at`). One per number: a new list replaces the old one.
   - `app.waha_inbound(event jsonb)`: turns a WAHA `message` event into Meta's webhook event (`entry[].changes[].value`
     with `contacts` and `messages`), so `app.wa_process_event` takes it as it is. A text that is only a number
     (Latin or Arabic-Indic digits) within the number's list of the last 24 hours becomes `type = interactive` with
     `button_reply` / `list_reply` `{ id, title }`. `fromMe` and groups are ignored. Media: the WAHA file's place is kept
     (`waha:` + its file id) instead of Meta's media id.
   - `app.waha_ack(event jsonb)`: a `message.ack` (SERVER 1 / DEVICE 2 / READ 3, ERROR -1) as Meta's `statuses`.
   - Grants as the other `app.wa_*` functions (n8n's role only; the API never reads them).
2. **Compose**: service `waha` under the profile `waha` (off unless asked), the image pinned to a release at least two
   weeks old, `WHATSAPP_DEFAULT_ENGINE=NOWEB`, `WAHA_API_KEY`, the dashboard user and password, the global webhook to
   `http://n8n:5678/webhook/waha-inbound` with the events `message,message.ack` and `WHATSAPP_HOOK_HMAC_KEY`; sessions in a
   volume on K:; port `127.0.0.1:3010` only (the QR code), never public. New `.env` keys (`gen-secrets` makes them):
   `WAHA_API_KEY`, `WAHA_HMAC_KEY`, `WAHA_DASHBOARD_PASSWORD`.
3. **n8n**:
   - `WAHA-INBOUND`: the webhook checks `X-Webhook-Hmac` (sha512 of the raw body) and drops anything not signed; then
     `app.waha_inbound` / `app.waha_ack`, then the same steps as `META-WA-INBOUND` (process event, quiet period, forward
     turns, fetch media).
   - `META-WA-SEND-MESSAGE`: after `Prepare` (every rule above already applied), a switch on `whatsapp.provider`. `waha` →
     text: `sendText`; interactive: the body and the numbered list as one text, saved to `app.waha_menus`; document:
     `sendFile` (from the signed storage link); the answer recorded through `app.wa_outbound_result` in Meta's shape
     (`messages[0].id` = WAHA's message id) so the API's send results stay the same.
   - `META-WA-SEND-TEMPLATE`: on `waha`, the template's body with its parameters as a text (only after the approval check
     passed, so in practice never before Meta's approval).
   - `WA-MEDIA-ONE`: a `waha:` media is downloaded from WAHA (with the API key), its sha256 computed here (receipts need
     it), then the same checks and storage as today.
   - `META-WA-ERROR-HANDLER`, the token check and the templates' sync stay Meta's (WAHA has neither).
4. **Tests**:
   - `wa-db-tests`: the event mapping (text, image, document, voice, a reply «1» / «١» / «3» out of range / after 24 hours,
     `fromMe`, a group), the acks, the menu replaced by a newer one.
   - The n8n harness: both channels' workflows import, the switch defaults to Meta, nothing calls WAHA while it is `meta`.
   - `meta-stub` answers WAHA's three send endpoints too, and `hazem-e2e` step 16 runs one booking through the WAHA path
     (the list as numbers, «1» back as the press), then switches back to Meta. Every other step stays on Meta.
   - Healthcheck: the setting is `meta` or `waha`; WARN while `waha`; WAHA's port not public.
5. **Docs**: `docs/runbooks/waha.md` (start it, scan the QR with the spare number, switch to `waha`, the test numbers,
   switch back to `meta` and stop it), `docs/whatsapp-integration.md`, `docs/env-variables.md`, DECISIONS v1.19 (T-03:
   «n8n بيكلّم Meta بس، وفي الاختبار WAHA على رقم احتياطي لحد موافقة Meta»), the session report.

## Not in this task

WAHA in production or on the office's number; WAHA's paid edition; groups, calls, statuses; changing anything in the API.

## Needs the owner

A spare number with WhatsApp on a phone, to scan the QR code once (WAHA keeps the session after that).
