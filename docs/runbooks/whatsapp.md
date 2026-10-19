# Runbook — WhatsApp (test environment)

Commands below use the Windows form (`scripts\windows\*.ps1`); Linux has the same names in `scripts/linux/*.sh`.

## Start / end of a test session

1. Docker Desktop running → `up` → `tunnel-refresh`.
2. Check: `cli wa-check` shows `callback_url = https://….trycloudflare.com/webhooks/whatsapp`.
3. End: `tunnel-down`.

## The tunnel URL changed (restart of the tunnel, Docker or the PC)

Symptom: messages to the test number never reach `app.wa_webhook_events`.
1. `tunnel-refresh` — reads the new URL, waits until it answers, calls `cli wa-subscribe`, then
   confirms with `cli wa-check`.
2. If Meta rejects it: `logs api` (look for the GET verification) and check that
   `WhatsApp__VerifyToken` in `.env` is the one the API runs with (`up` after editing `.env`).
3. Manual fallback: Meta App → WhatsApp → Configuration → Webhook → Callback URL
   `https://<tunnel>/webhooks/whatsapp`, Verify token = `WhatsApp__VerifyToken`, subscribe field `messages`.

## Access token expired or revoked (Meta error 190)

Symptom: Telegram alert "access token is invalid", sends fail with `TOKEN_INVALID`.
1. Business Settings → System users → generate a new token (docs/meta-setup.md, part 2.3).
2. Put it in `.env` as `WhatsApp__AccessToken=…` (never in chat or files).
3. `up` (recreates the API with the new value) → `cli wa-check`.

## Meta is not sending events

1. `cli wa-check`: `callback_url` correct? `active: true`? `waba_subscribed_apps` contains the app?
   If not: `cli wa-subscribe-waba`, then `tunnel-refresh`.
2. Is the sender in the recipient list of the **test number**? Only listed numbers can message it.
3. `logs api`: 401s mean signature mismatch → `WhatsApp__AppSecret` is wrong.

## Template rejected or re-categorised

1. `cli templates-sync` → see `status`, `rejection_reason`, `category_changed_by_meta`.
2. Fix the text in `whatsapp-templates.json` under a **new name** (Meta does not allow resubmitting
   a rejected name for a while), rebuild (`backend-build`), `cli templates-submit`.

## Rate limits (RATE_LIMIT)

HTTP 429 is retried automatically (exponential backoff). Persistent limits on the test number are
normal (low quotas). Lower the test volume; real limits rise after Business Verification.

## Signature failures alert

20+ POSTs with bad signatures in 5 minutes. Someone is hitting the public tunnel URL, or the App
Secret changed. Check `logs api`; rotate the URL with `tunnel-down` + `tunnel-refresh`; verify
`WhatsApp__AppSecret`.

## Moving the office's real number (plan only — needs the project owner's explicit go-ahead)

Do this only after Hazem is built and tested on the test number, and after Business Verification.
1. Back up the office WhatsApp chats on the phone (Settings → Chats → Chat backup).
2. Choose **Coexistence** (keeps the WhatsApp Business app on the phone; check it is offered for this
   number/country at that time) or **Migration** (API only; old chats do not move).
3. Add the number to the production WABA, verify it, and get the display name
   «مكتب الأستاذ سعد فتحي سعد» approved.
4. Production needs a stable HTTPS domain (T-13) instead of the Quick Tunnel.
5. Update `WhatsApp__PhoneNumberId` / `WhatsApp__WabaId`, run `cli wa-subscribe-waba`, `cli wa-check`,
   resubmit templates to the production WABA, and test with one internal number before announcing.
