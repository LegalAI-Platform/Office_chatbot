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
2. If step 7 fails: open n8n → `META-WA-INBOUND` → check it is published and has no errors;
   run `META-WA-SUBSCRIPTION-CHECK` by hand and read `callback_url`.

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

## End-to-end test without Meta

`hazem-e2e`: message → n8n → API → reply (dry run) → result back; same turn twice; API down and
back; API → n8n send; wrong tokens. Uses dummy numbers 20100000006x and switches dry run and the
API echo off again at the end. `backend-test` runs the .NET unit and integration tests.

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
