# TASK 15 — clients with a WhatsApp username (no phone number), DECISIONS v1.37

**Status: a plan for the owner to review. Nothing is built.** The owner's «Plan it» (2026-10-31), after a test phone's
messages never got an answer.

## 1. What happened

The test phone ending in 90 sent «السلام عليكم» to the bot from Wednesday on; the messages were delivered (✓✓) but
Hazem never answered. That phone's WhatsApp account has a **username**. Meta then sends the message **without the
phone number**: `messages[].from` and `contacts[].wa_id` are missing, and `from_user_id`, `contacts[].user_id` and
`contacts[].profile.username` come instead (seen in n8n's failed run on the server, keys only). `app.wa_process_event`
puts `from` into `wa_inbound_messages.wa_id` (NOT NULL), the insert fails and the whole event is lost: **12 messages
since 2026-10-28, with no alert to anyone**. Every real client who adopts a username would be silently ignored.

## 2. Meta's rules (developers.facebook.com, «Business-scoped user IDs»)

- **BSUID** (business-scoped user ID): `EG.` + up to 128 letters and digits, one per user per business portfolio,
  changed only when the user changes his phone number (then a `user_changed_user_id` system message carries
  `previous_user_id`). In webhooks since April 2026, **also for users without a username**.
- **Inbound** with a username and the number hidden: `contacts[].user_id`, `contacts[].profile.name`,
  `contacts[].profile.username`; `messages[].from_user_id`. No `from`, no `wa_id`.
- **The number is still sent** when, in the last 30 days, the business messaged or called that number, received a
  message or call from it, or has it in its contact book.
- **Sending to a BSUID:** the `recipient` field instead of `to` (if both, `to` wins). Every message type works,
  **templates included**, except authentication templates (error `131062`). Expected from July 2026.
- **Statuses:** `recipient_user_id` is always the BSUID; `recipient_id` (the number) may be missing.
- **Asking for the number:** a `REQUEST_CONTACT_INFO` button (from July 2026).
- **Not stated in the docs, to be checked on the test number:** whether Meta's test number answers a BSUID (its
  recipient list is phone numbers), and how the 24-hour window counts for a BSUID.

## 3. Where our system assumes a phone number

- **The database:** `wa_id ~ '^[0-9]{8,15}$'` in `app` (inbound, outbound, turns, windows, statuses, WAHA menus) and
  `hazem` (persons, turns, lists, Chatwoot threads); `wa_outbound_prepare` refuses anything else; test numbers
  (`whatsapp.test_recipients`, `hazem.recipients`) are phone numbers.
- **n8n:** the inbound event, the 8-second grouping, and sends with `"to": wa_id`.
- **.NET:** `WaId` is 8–15 digits; the person, the conversation, the lists (B-14), Chatwoot's contact.
- **What really needs a number:** the lawyer's phone call (B-03, B-09 urgent: «المستشار هيكلم حضرتك على الرقم ده»),
  the number shown to the office (alert, calendar, panel), and authentication templates (not used).

## 4. The proposal

**Phase A — never lose a message again (small, first).** `wa_process_event` keeps every event: a message with no
`from` is stored with its BSUID and marked, and the managers get an alert (bell and e-mail, no client data) instead of
a silent failure. Can go out on its own.

**Phase B — the client's identity is «phone or BSUID».**
- One contact key everywhere: the phone number when Meta sends it, else the BSUID. The client row keeps both
  (`wa_id` and a new `user_id`) and the username. When a client who first came as a BSUID later shows his number
  (Meta sends both together), the two are joined into one client — never two clients for one person.
- n8n sends with `recipient` when the key is a BSUID, `to` otherwise. Statuses matched by `recipient_user_id` too.
- Test mode: `whatsapp.test_recipients` / `hazem.recipients` also accept a BSUID or a `@username`.
- The office sees: the name he gave, else his WhatsApp name «(اسم الواتساب)» (v1.36), else `@username`; and the
  number when known, else «من غير رقم».

**Phase C — the number when a call needs it.** A phone or urgent consultation for a client without a number: Hazem
asks for the number the lawyer should call before holding the time (as B-03's «رقم تاني» already does). The reminder,
the rating, the documents: no number needed.

## 5. Decisions for the owner (my recommendation first)

1. **Answer username clients fully** (welcome, services, booking, payment) — recommended; the other way loses them.
2. **The phone or urgent call without a number:** Hazem asks for it, proposed text:
   > المستشار هيكلم حضرتك على أنهي رقم؟ اكتبه هنا من فضلك.

   Office meeting and written services: no question. (Alternative: Meta's `REQUEST_CONTACT_INFO` button, once
   checked on the test number.)
3. **Test numbers by `@username`:** you write the test phone's username in the panel (أدوات التجربة) — recommended,
   since a BSUID is not something you can read on the phone.
4. **Phase A first, alone, now**, then B and C together.

## 6. Done when

- A message from a username account is never lost: stored, and answered (or, in test mode, refused with the reason
  visible to the office).
- The test phone ending in 90 talks to Hazem end to end: welcome, booking, receipt, the lawyer's urgent alert, a
  reminder.
- A client who appears first as a BSUID and later with his number is one client, his history whole.
- Every check passes (backend, web, wa-tests with a BSUID fixture, e2e, healthcheck), and the panel shows `@username`
  and «من غير رقم» correctly.

## 7. Risks

- Meta's test number may not deliver to a BSUID: then the full test waits for the office's own number (TASK 05b).
- Large change to the database keys: done with a migration that changes no existing phone number, and checked on a
  copy of the server's data first.
