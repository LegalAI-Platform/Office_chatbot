# WhatsApp integration (TASK 02)

## Endpoints

| Method | Path | Behaviour |
|---|---|---|
| GET | `/webhooks/whatsapp` | `hub.mode=subscribe` + correct `hub.verify_token` → returns `hub.challenge` (200). Otherwise 403. 503 while `WhatsApp__Enabled=false`. |
| POST | `/webhooks/whatsapp` | Verifies `X-Hub-Signature-256`; stores events; queues processing; 200. Bad signature 401, body > 1 MB 413, invalid JSON 400, non-WhatsApp object 200 (ignored). Rate limit 600/min. |
| GET | `/health/live`, `/health/ready` | Liveness; readiness checks PostgreSQL and Redis. |

## Background jobs (Hangfire, schema `hangfire`)

| Job | When | Notes |
|---|---|---|
| `ProcessEventAsync(eventId)` | per new webhook event | 3 retries (10 s, 60 s, 5 min); Telegram alert after the last |
| `SyncTemplatesAsync` | every 6 hours (`0 */6 * * *`) | pulls template status/category from Meta |
| `EchoTestAsync` | every minute | does nothing unless `WhatsApp__EchoTest__Enabled=true` **and** environment `test` |

## Tables (schema `app`)

| Table | Purpose |
|---|---|
| `wa_webhook_events` | one row per change; unique `payload_sha256`; `received → processed / ignored / failed` |
| `wa_inbound_messages` | unique `wamid`; `pending` for TASK 03, `ignored_group` for groups; `referral` kept (B-13) |
| `wa_contact_windows` | last inbound message per `wa_id`; only moves forward (M-02) |
| `wa_outbound_messages` | unique `idempotency_key` and `wamid`; `queued → sending → sent → delivered → read`, `failed`, `blocked_window` |
| `wa_message_statuses` | every delivery status from Meta; unique `(wamid, status, status_at)` |
| `wa_templates` | template status/category synced from Meta |

## Use cases (Office.Core)

**`MessageSender.SendTextAsync(waId, text, idempotencyKey, requestedBy, contextWamid?)`**
**`MessageSender.SendTemplateAsync(waId, templateName, language, components?, idempotencyKey, requestedBy)`**

Returns `SendResult { Ok, OutboundId, Wamid, Status, ErrorCode, ErrorMessage, Duplicate }`.
`waId`: 8–15 digits. Text: 1–4096 characters (the 300/600-character style rule B-21 belongs to TASK 04).

| Error code | Meaning | Meta call made? |
|---|---|---|
| `OUTSIDE_24H_WINDOW` | free-form message ≥ 24 h after the customer's last message (or Meta 131047) | no (local check) |
| `TEMPLATE_NOT_APPROVED` | template not `APPROVED` in `wa_templates` | no |
| `TOKEN_INVALID` | Meta 190 — token expired/revoked; operator alerted | yes |
| `RATE_LIMIT` | Meta 130429 / 131056 / 80007 / 4 | yes (retried on HTTP 429) |
| `UNDELIVERABLE` | Meta 131026 | yes |
| `RECIPIENT_NOT_ALLOWED` | Meta 131030 — test number can only send to its recipient list | yes |
| `META_ERROR` / `NETWORK_OR_UNKNOWN` | any other Meta error / no response | yes |
| `WHATSAPP_DISABLED` | `WhatsApp__Enabled=false` | no |

A repeated `idempotencyKey` returns the first result with `Duplicate = true` and never calls Meta again.

## Office.Cli

| Command | Effect |
|---|---|
| `migrate [--to X]`, `migrations-list` | EF migrations + Hangfire schema, as `office_migrator` |
| `wa-subscribe --callback URL` | registers the app webhook (`whatsapp_business_account`, field `messages`) with the app token |
| `wa-subscribe-waba` | subscribes the app to the WABA's events |
| `wa-check` | current callback URL, WABA subscribed apps, phone number status |
| `templates-submit` | submits catalog templates Meta does not have (never resubmits or deletes) |
| `templates-sync` | pulls statuses into `wa_templates`, flags category changes by Meta |
| `send-test --to WA_ID [--text …]` | sends only to `WhatsApp__TestRecipients` |

## Templates (M-03)

`backend/src/Office.Infrastructure/WhatsApp/Templates/whatsapp-templates.json` — 11 templates,
`ar`, category `UTILITY`, each variable with an example. Some bodies were adjusted so they do not start
or end with a variable (Meta rejects those). Meta may re-categorise a template (e.g. `booking_followup`
as `MARKETING`); `templates-sync` reports it.

## The 24-hour window (M-02)

Free-form text is allowed only while `now − last_inbound_at < 24 h`. Outside it, callers must send an
approved template. Group messages never open a window, so the office never replies into groups.

## Limits and known constraints

- **Quick Tunnel (D-5):** the URL changes on every tunnel/Docker/machine restart. Run `tunnel-refresh`.
  Events Meta sends while the tunnel is down are retried by Meta for a limited time, then lost.
- **One Meta App = one callback URL.** Production needs its own app or a re-subscription.
- **Invalid signatures** are rejected and counted; 20+ in 5 minutes triggers an alert.
- **Data Protection keys** are not persisted yet (harmless now; required before cookies/Identity in TASK 07).
