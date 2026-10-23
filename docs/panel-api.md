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

- `two_factor_required` (v1.9): a manager (`users.manage`) without the authenticator, once
  `PANEL_SECRET_KEY` is set. Every endpoint except `me`, `logout`, `csrf`, `change-password`
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
