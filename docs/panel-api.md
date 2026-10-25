# Panel API (`/api/panel`, TASK 07a, DECISIONS v1.9 T-11 / T-23)

The panel (React, `web/`) talks to this API only. nginx in the `web` container forwards
`/api/panel/*` to `http://api:8080` and answers **404 for every other `/api` path**, so n8n's
endpoints (`/api/hazem/*`, `X-Hazem-Token`) are never reachable from the panel.

- JSON is snake_case, like the rest of the API. Times are UTC ISO-8601; the panel shows them in
  Africa/Cairo.
- Every response that holds client data has `Cache-Control: no-store`.
- Errors: `{ "error": "<code>" }` (and `errors: { field: [...] }` for field errors). The panel
  maps each code to an Arabic sentence (`web/src/shared/api/client.ts`).

## Sign-in, session and antiforgery

| Endpoint | Who | What |
|---|---|---|
| `GET /auth/csrf` | anyone | `{ token }`: send it as `X-CSRF-TOKEN` on every POST. It is tied to the signed-in user: fetch a new one after login and logout. A refused token answers `400` with `X-Antiforgery-Failed: 1` |
| `POST /auth/login` | anyone | `{ username, password }` → `200` + the user (as `/auth/me`). Wrong name, wrong password, a disabled account and a locked account all answer the **same** `401 { error: "invalid_credentials" }`. 5 wrong passwords lock the account for 15 minutes. At most 10 attempts per minute per address (`429`). **v1.9:** with the authenticator on, `200 { two_factor: true, ticket }` and no session yet; a registered account that still waits answers, only after the right password, `403 { error: "email_not_verified", email }` or `403 { error: "pending_approval" }` |
| `POST /auth/login/2fa` | anyone | `{ ticket, code }`: the six digits from the app, or a recovery code (`xxxx-xxxx`, once each). The ticket lives 5 minutes (`401 { error: "expired_ticket" }`). A wrong code → `401 { error: "invalid_code" }` and counts towards the lockout |
| `POST /auth/logout` | signed in | ends this session |
| `GET /auth/me` | signed in | `{ id, username, display_name, email, role, permissions[], must_change_password, two_factor, two_factor_required }` |
| `POST /auth/change-password` | signed in | `{ current_password, new_password }`. At least 12 characters (no symbol rules, NIST). Signs every other session out; this one gets a new cookie |

- Cookie `hazem_panel`: HttpOnly, SameSite=Strict, Secure (except plain http in `ENVIRONMENT=test`),
  12 hours without use. Each request re-reads the account: a disabled account, a new password
  or a new role (the security stamp changes) ends every session at once.
- `must_change_password` (a temporary password): every endpoint except `me`, `change-password`,
  `logout` and `csrf` answers `403 { error: "must_change_password" }`.

- `two_factor_required` (v1.9; **off since v1.16** unless `PANEL_MANAGERS_NEED_2FA=true`): a manager
  (`users.manage`) without the authenticator, once `PANEL_SECRET_KEY` is set. Every endpoint except `me`, `logout`, `csrf`, `change-password`
  and `/auth/2fa/*` answers `403 { error: "two_factor_required" }` until it is on. The panel
  sends the person to `/setup-2fa`.

## Sign-up and a forgotten password (v1.9, anyone)

Every one of these shares the sign-in rate limit (10 a minute per address). E-mails come from the
no-reply address (`SMTP_*`); codes are six digits, 10 minutes, 5 tries, once, at most one a minute
and five an hour, kept only as a keyed hash (`PANEL_SECRET_KEY`). Without the key or the mail
server, these answer `503 { error: "not_configured" }`.

| Endpoint | What |
|---|---|
| `GET /auth/signup` | `{ available }`: the key and the mail server are set |
| `POST /auth/register` | `{ username, display_name, email, password }` → `202 { mail_sent }`; the code goes to the e-mail. A taken user name → `400` with `errors.username = ["username_taken"]`. A taken **e-mail** answers the same `202` (no account is made; the owner of the address gets a note instead of a code). The account is pending: inactive, no role, no way in |
| `POST /auth/register/resend` | `{ email }` → `202`, always (a code only for a pending, unconfirmed address) |
| `POST /auth/register/verify` | `{ email, code }` → `204`; every active manager gets an e-mail. Wrong or old code → `400 { error: "invalid_code" }`. A registration not confirmed within a day is deleted when someone registers the same name or address |
| `POST /auth/forgot` | `{ login }` (user name or e-mail) → `202`, always; a code only for an active account |
| `POST /auth/reset` | `{ login, code, new_password }` → `204`: unlocks the account, ends every session, e-mails the owner |

## The authenticator app (v1.9, signed in)

| Endpoint | What |
|---|---|
| `GET /auth/2fa` | `{ enabled, recovery_codes_left, available }` |
| `POST /auth/2fa/setup` | `{ secret, uri }` (`otpauth://`, for the QR code). The secret is kept encrypted until a right code turns it on |
| `POST /auth/2fa/enable` | `{ code }` → `{ recovery_codes[10] }`, shown once. Every other session ends |
| `POST /auth/2fa/disable` | `{ password, code }` (app or recovery code) → `204`. Every other session ends |
| `POST /auth/2fa/recovery-codes` | `{ code }` → ten new codes; the old ones stop working |

## Alerts, files and the office's tools (TASK 07c, DECISIONS v1.11)

| Endpoint | Permission | What |
|---|---|---|
| `GET /alerts` | view_own | `{ unread, items[30] }`: the alerts of tasks this person may see (overdue, urgent, a client asking for someone), newest first, with `read` |
| `POST /alerts/read` | view_own | `{ ids }` or `{ ids: null }` (all) |
| `GET /tasks/{id}/files/{n}` | view_own (and may see the task) | the file of the task turn's n-th message, from storage (read-only key). Pictures, sound, video and PDF open in the browser; anything else downloads as `client-file-<n>.<ext>`. `Content-Security-Policy: sandbox`, never cached. Every opening → `panel_audit task.file_open` (no file name) |
| `GET /lists?kind&q&page` | lists.manage | `{ total, items }`, 50 a page; `q` = part of the name or 3+ last digits |
| `POST /lists` | lists.manage | `{ kind, phone, name, staff_name, note }` (adds or updates by kind + number) |
| `DELETE /lists/{id}` | lists.manage | |
| `POST /lists/import` | lists.manage | `{ kind, csv, replace }` → `{ imported, rejected[] }` (as `import-lists`) |
| `GET /conversations/muted` | conversations.unmute | the muted conversations (S-01) |
| `POST /conversations/{wa_id}/unmute` | conversations.unmute | back to Hazem |
| `GET /texts` · `GET /texts/{key}` | texts.manage | the active version of each text · every version of one |
| `POST /texts/{key}` | texts.manage | `{ text }` → a new active version; `too_long` (over its 300/600, B-21), `placeholder` ({{…}}) |
| `POST /texts/{key}/versions/{v}/activate` | texts.manage | an earlier version back |
| `GET /reports?month=YYYY-MM` | reports.view | the month (Cairo): tasks by type and staff member, done on time, new clients by source, topic and kind |

Alerts are made every minute by the api (tasks past their due time become `overdue` first) and
e-mailed once to everyone who may see the task; the e-mail holds the task's kind, due time and a
link, never the client's name, number or words.

## The booking (TASK 08, DECISIONS v1.12 B-24)

Minutes of the Cairo day: 0–1440 (1440 = midnight at the end of the day). A change that leaves
confirmed appointments outside the hours answers `409 { error: "has_bookings", appointments }`;
the same request with `confirm: true` cancels them (`cancelled_by_office`) and Hazem tells each
client (B-22) → `{ cancelled, not_told }`. Every change: `booking_changes` and `panel_audit`.

| Endpoint | Permission | What |
|---|---|---|
| `GET /booking/week` · `PUT /booking/week` | schedule.manage | `[{ weekday (0 = Sunday), ranges: [{ start, end }] }]` · `{ days, confirm }`; `overlapping_ranges`, `invalid_range` |
| `GET /booking/month?month=YYYY-MM` | schedule.manage | per day: `status` (open, weekly_off, closed, custom_hours, partly_closed), the hours, confirmed bookings, the exceptions |
| `POST /booking/exceptions` | schedule.manage | `{ day, kind: closed_day\|closed_hours\|custom_hours, start?, end?, reason?, confirm? }`; `past_day` |
| `DELETE /booking/exceptions/{id}?confirm=true` | schedule.manage | `404 not_found` |
| `GET /booking/settings` · `PUT /booking/settings` | schedule.manage | `{ enabled, buffer_minutes, min_notice_hours, horizon_days, max_per_day, slot_step_minutes, offer_count, calendar_id }` (+ read only `calendar_key`, `texts_approved`); a value out of range → its name as the error |
| `GET /booking/changes` · `POST /booking/undo` | schedule.manage | the last 30 changes · `{ confirm }`: the last one put back (`409 nothing_to_undo`) |
| `GET /booking/appointments?from&to` | schedule.manage | up to 92 days: service, start, end, state (S-03), name, number, topic, changes, calendar state. **TASK 12:** `attending` («هحضر ✅» for this time) and `reminders` of this time: `[{ kind: day_before\|two_hours, state: scheduled\|sent\|failed\|skipped\|cancelled, due_at, sent_at, channel: message\|template, skip_reason: too_close\|passed\|late\|off, error_code }]` |
| `POST /booking/appointments/{id}/state` | schedule.manage | `{ state: completed\|no_show }` |
| `POST /booking/appointments/{id}/cancel` | schedule.manage | cancelled by the office → `{ cancelled }`; the client is told by the worker (B-22) |
| `GET /booking/preview?day&service=pv\|of\|ur` | schedule.manage | `{ calendar: ok\|unavailable\|not_configured, slots[] }`: what Hazem would offer that day now |
| `GET /office` · `PUT /office` | office.manage | P1: `{ instapay, wallet_number, wallet_provider, address, map_url (https) }` |

TASK 09 (v1.13): `/booking/settings` also carries the hold: `hold_minutes` (15–1440), `hold_short_minutes`
(15–hold_minutes), `hold_short_within_hours` (0–72), `hold_gap_minutes` (0–240); an appointment carries
`hold_until` and `payment` (S-04). States: `pending_payment` (held until the receipt), `expired` (the hold ran out).

Every change to the hours (the week, an exception added or removed, undo) answers
`409 { error: "has_bookings", appointments }` when it would leave confirmed appointments outside
the hours; sent again with `confirm: true` (`?confirm=true` for DELETE) the change and the
cancellations are one transaction → `200 { cancelled }`. No message is sent inside the request:
the booking worker (every 10 s) tells each client (`client_notice` pending → sent | failed),
with the free times and «استرداد كامل» when booking is on, or the text alone and a task
`office_cancelled` when it is off.

## Payments (TASK 09, B-05 / B-22 / S-04)

The system never confirms a payment and never says a refund was made: a person does both.

| Endpoint | Permission | What |
|---|---|---|
| `GET /payments?show=review\|refunds\|all` | schedule.manage | `review` (default): receipts waiting for a person, the oldest first; `refunds`: refunds to transfer; `all`: the last 60 days. Each: the appointment, `state`, `amount_due`, `amount_received`, `proof_at`, `receipts`, `duplicate`, who reviewed / refunded and when, `refund_due`, `refund_reason` |
| `GET /payments/{appointment}/receipts/{n}` | schedule.manage | the n-th receipt image (0 = the first), like a client's file in 07c: `no-store`, CSP sandbox, every opening in the audit (`payment.receipt_open`) |
| `POST /payments/{appointment}/review` | payments.verify (all roles, P1) | `{ state: verified\|partial\|not_received, received }`: `verified` needs `received` ≥ due (more → `refund_requested` for the difference, P4, and a `refund` task); `partial` 0 < received < due. The three marks may correct each other until a refund starts. `400 invalid_amount`, `409 not_allowed` |
| `POST /payments/{appointment}/refunded` | payments.refund (all roles, P2) | `{ method, note? }`: only from `refund_requested`, once (`409`) |

Partial, not received and refunded are told to the client by the booking worker (every 10 s), never
inside the request; a send that fails (outside the 24-hour window too, P5) becomes a `booking_message_failed` task.
`GET /reports` (07c) also has `by_booking` (the month's appointments by state), `received`, `missing` and `refunded` (pounds).

## Service orders (TASK 10, DECISIONS v1.14 B-16 / B-17 / S-07)

A written service (contract review, contract drafting, formal notice, written opinion) from the client's
documents to the delivered file. States: `collecting` → `awaiting_price` → `awaiting_payment` →
`in_progress` → `delivered`, and `closed` from any open state.

| Endpoint | Permission | What |
|---|---|---|
| `GET /services?show=open\|done` | services.manage (all roles) | the orders: `kind`, `state`, the client, `files` (how many documents), `price`, `price_exception`, `due_on`, `delivery` (`file`\|`call`), `price_until`, `payment` (the payment's state), `file_name`, `delivered_at`, `closed_reason`, `appointment_id`, `notice` (`pending`|`sent`|`failed`: the last message to the client) |
| `GET /services/{id}/files/{n}` | services.manage | the n-th document the client sent, like a client's file in 07c: `no-store`, CSP sandbox, every opening in the audit (`service.file_open`) |
| `POST /services/{id}/price` | **services.price** (manager, lawyer: decision b) | `{ price, exception, due_on, delivery }`: from `collecting` / `awaiting_price`. B-10's range (review 1,500–3,000, drafting from 3,000, notice and opinion 2,000); outside it only with `exception: true` (`400 out_of_range`). The price holds 3 days (decision c) |
| `POST /booking/appointments/{id}/opinion` | services.price | `{ due_on, delivery }`: B-17, a written opinion (2,000) from a **completed** consultation; once per booking (`409`) |
| `POST /services/{id}/upload` | services.manage | `{ filename }` (pdf, doc, docx) → `{ url, key, content_type }`: a link signed for 10 minutes to PUT the file to `office-sensitive/deliverables/…` through the panel's nginx (`/storage/…`). Only `in_progress` with delivery `file` |
| `POST /services/{id}/deliver` | services.manage | `{ key, filename }` for a file (the API checks it is there with HEAD, ≤ 20 MB, under that order's key, the same extension as the file uploaded: `400 no_file | too_large | invalid`), `{}` for a call. → `delivered`; the worker sends the file with its caption, or the caption alone after a call |\| too_large`), `{}` for a call. → `delivered`; the worker sends the file |
| `POST /services/{id}/close` | services.manage | closes an open order; money a person found (verified, partial) opens a refund (`refund` task, P2 as in TASK 09). A receipt nobody reviewed yet promises nothing: if its review later finds the money, that opens the refund. `409` if already closed or delivered |
| `POST /services/{id}/resend` | services.manage | «ابعت الملف تاني»: a delivered file sent again with a new key (the first send fell outside the 24-hour window, or the client lost it). `409` while a send is waiting |

The order's receipt is reviewed on **الإيصالات والمدفوعات** like a booking's (`/payments/{order id}/review`,
`/refunded`): «تمام» starts the work (`in_progress`). The client hears every step from the worker (every 10 s),
never inside the request. The API never receives a file: the browser PUTs it to storage with the signed link
(decision a), and n8n sends Meta a signed read link (one hour).

## Urfi cases (TASK 11, DECISIONS v1.15 B-11 / S-06)

Hazem asks B-11's questions on WhatsApp (buttons; the ages read by the API, never the AI), then the price, the documents
and the free times in the office; the pick holds the time until the receipt (B-08); after the receipt the client sends
the documents. States: `collecting` → `documents_pending` → `booked` → `approved` / `lawyer_rejected`, or closed early
(`rejected_age`, `rejected_guardian`, `redirected_consultation`).

| Endpoint | Permission | What |
|---|---|---|
| `GET /urf?show=open\|done` | urf.manage (all roles) | the cases: `kind` (marriage / divorce), `state`, the client, the answers (`husband_age`, `wife_age`, `wife_past`, `guardian`, `witnesses`, `has_copy`, `agreed`, `children`), `documents` (what is needed, in words), `files`, the appointment (`start`, `appointment`, `payment`), `decided_by`, `decided_at`, `notice` |
| `GET /urf/{id}/files/{n}` | urf.manage | the n-th document (an ID card), like a client's file in 07c: `no-store`, CSP sandbox, every opening in the audit (`urf.file_open`) |
| `POST /urf/{id}/review` | **urf.review** (manager, lawyer) | `{ decision: approved\|rejected }`. `approved` (only `booked` with its appointment still on): the worker sends B-11.7's confirmation with who must come. `rejected` (`documents_pending` or `booked`): the appointment `cancelled_by_office` without B-22's list, its event removed, what was paid due back in full (`refund` task, P2), and the worker tells the client. Every decision is kept with `policy_version`. `409` otherwise |

`/booking/settings` also carries `urf_minutes` (15–480, default 60): the length of an Urfi appointment.

## Chatwoot (TASK 07b, T-24)

| Endpoint | Permission | What |
|---|---|---|
| `POST /chatwoot/open` | account.self | `{ task_id? }` → `{ login_url, conversation_url }`: a one-time link that signs this person into Chatwoot (their Chatwoot user is made first if missing), and the client's conversation when the task is one they may see (`404` otherwise). `503 { error: "not_configured" \| "chatwoot_unavailable" }` |

Chatwoot's webhook (`POST /chatwoot/webhook`) is not under `/api/panel` and is not reachable
through nginx: only Chatwoot calls it, on the Docker network, signed with `CHATWOOT_WEBHOOK_TOKEN`
(`X-Chatwoot-Signature`, 5 minutes).

## Permissions (RBAC, T-23)

The code checks permissions, never role names. A role is a set of permissions, defined once in
`Office.Application/Panel/Permissions.cs`.

| Permission | manager | lawyer | staff |
|---|---|---|---|
| `tasks.view_own` (own tasks and unassigned ones) | ✅ | ✅ | ✅ |
| `tasks.view_all` | ✅ | ✅ | |
| `tasks.update` (start, done, cancel) | ✅ | ✅ | ✅ |
| `tasks.assign` | ✅ | ✅ | |
| `users.manage` | ✅ | | |
| `account.self` (me, change password) | ✅ | ✅ | ✅ |
| `lists.manage`, `texts.manage`, `reports.view` (TASK 07c) | ✅ | ✅ | |
| `conversations.unmute` (TASK 07c) | ✅ | | |
| `schedule.manage` (TASK 08: the booking hours, the appointments) | ✅ | ✅ | ✅ |
| `office.manage` (TASK 08: InstaPay, the wallet, the address) | ✅ | ✅ | |
| `payments.verify` (TASK 09, P1: a transfer is fine / partial / not received) | ✅ | ✅ | ✅ |
| `payments.refund` (TASK 09, P2: «اتحوّل», a refund made) | ✅ | ✅ | ✅ |
| `services.manage` (TASK 10: the orders, their documents, the delivery, closing) | ✅ | ✅ | ✅ |
| `services.price` (TASK 10, decision b: the price of a service, the written opinion) | ✅ | ✅ | |
| `urf.manage` (TASK 11: the Urfi cases and their documents) | ✅ | ✅ | ✅ |
| `urf.review` (TASK 11: approve or refuse a case before the appointment) | ✅ | ✅ | |

No permission → `403`. Not signed in → `401`. A task the user may not see → `404`.

## Tasks (S-05)

| Endpoint | Permission | What |
|---|---|---|
| `GET /tasks?scope=mine\|unassigned\|all&state=active\|overdue\|done\|cancelled&page=1` | view_own (`all` needs view_all) | 25 per page. `active` = open, in progress, overdue (by due time); done / cancelled by last change |
| `GET /tasks/{id}` | view_own | the task, the client (name as written, full number, client type, source B-13, Hazem's state S-01), the topic (B-25), the **client's own words** from the turn that opened it (text, caption, finished transcript, and only the *type* of any file — never a bucket, key or file name), and `can { start, done, cancel, assign }` |
| `POST /tasks/{id}/start` · `/done` · `/cancel` | update | through the S-05 machine. A move it refuses, or a task someone else changed a moment ago → `409 { error }`. A staff member who starts or finishes an unassigned task gets it |
| `POST /tasks/{id}/assign` | assign | `{ staff_id }` (`null` = unassigned) |
| `GET /staff` | assign | active staff members `[{ id, name }]` |

## Accounts (`users.manage`: the manager)

| Endpoint | What |
|---|---|
| `GET /users` | every account (never a hash); pending ones first, with `pending`, `email_verified`, `role: null`; and `two_factor`, `created_at` |
| `POST /users` | `{ username, display_name, email, role, staff_name }` → `{ user, temporary_password }`. The temporary password is shown **once**; the account must change it at first sign-in. A staff account is linked to `hazem.staff` by `staff_name` (created if missing) |
| `POST /users/{id}/disable` · `/enable` | disable ends every session. Not on yourself |
| `POST /users/{id}/role` | `{ role }`. Ends that account's sessions. Not on yourself |
| `POST /users/{id}/reset-password` | `{ temporary_password }`, shown once; unlocks the account |
| `POST /users/{id}/approve` | v1.9: `{ role, staff_name }` → the account can sign in; it gets an e-mail. Only a pending account with a confirmed e-mail (`409 { error: "not_pending" \| "email_not_verified" }`) |
| `POST /users/{id}/reject` | v1.9: a pending registration is deleted (the name and address are free again); it gets an e-mail if it was confirmed |
| `POST /users/{id}/reset-2fa` | v1.9: turns the authenticator off for someone who lost their phone; ends their sessions. Not on yourself |

The last active manager can never be disabled or demoted (`409 { error: "last_manager" }`).
A pending account is approved, never enabled or given a role directly (`409 { error: "pending_approval" }`).
Every change is written to `hazem.panel_audit` (who, what, when; never a password or client text).

## From the terminal (first manager and emergencies)

`Office.Cli create-user`, `reset-password`, `unlock-user`, `reset-2fa`, `list-users`: see
[runbooks/panel.md](runbooks/panel.md).
