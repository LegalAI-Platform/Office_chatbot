# Runbook: the panel (TASK 07a)

The panel is at **http://127.0.0.1:8080** (container `web`). It is reachable from this machine
only until TASK 14. Accounts and permissions: DECISIONS v1.9 T-23; the API: [panel-api.md](../panel-api.md).

The commands run the API's CLI inside the `api` container (the same on Windows and Linux):

```powershell
docker compose exec -it api dotnet /app/cli/Office.Cli.dll <command> <options>
```

Passwords are **typed in the terminal** (twice, hidden) and never shown, never put on the command
line, and never sent in a chat.

## First manager (once, by the owner)

```powershell
docker compose exec -it api dotnet /app/cli/Office.Cli.dll create-user --name owner --display "اسمك" --email you@example.com --role manager
```

Type the password twice when asked (12 characters or more; a long sentence is easier to remember
than a complex word). Then open the panel and sign in. From now on the manager adds every other
account from **الحسابات** in the panel; the terminal is only for emergencies.

## Adding a staff member (the manager, in the panel)

**الحسابات → ضيف حساب**: name, user name, e-mail (Chatwoot will need it in 07b), role, and for a
staff member the name used in the office lists. The panel shows a **temporary password once**:
give it to the person yourself. They must change it the first time they sign in.

## Someone signs up (v1.9)

Anyone can open **اعمل حساب جديد** on the sign-in page, confirm the e-mail with the six digits
it receives, and then wait: the account cannot sign in. Every manager gets an e-mail. In the
panel: **الإعدادات → الحسابات → مستنيين التفعيل**: pick the role (and, for a staff member, the
name used in the office lists) and **فعّل الحساب**, or **ارفض** (the request is deleted). The
person gets an e-mail either way. A request not confirmed within a day is dropped.

Needs, in `.env`: `PANEL_SECRET_KEY` (`gen-secrets`) and the mail server (`SMTP_HOST`,
`SMTP_PORT=587`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM=no-reply@…`), then
`docker compose up -d api`. Without them the sign-up page says it is not available.

## Two-step sign-in (the authenticator app, v1.9)

**Optional for everyone, the manager too** (v1.16, the owner's decision; it needs `PANEL_SECRET_KEY`). With
`PANEL_MANAGERS_NEED_2FA=true` in `.env` a manager must again (v1.9): at the next sign-in the panel opens
«شغّل التحقق بخطوتين» and nothing else works until it is on. Each person turns it on from **حسابي → التحقق بخطوتين**: scan the QR code with Google or
Microsoft Authenticator, type a code, and keep the ten recovery codes (shown once). From then on,
sign-in asks for the code after the password. A lost phone: sign in with a recovery code, or the
manager uses **اقفل التحقق بخطوتين (موبايل ضاع)** on that account. If `PANEL_SECRET_KEY`
changes, everyone who had it on must set it up again (the manager turns it off for them).

## Someone forgot their password, or is locked out

- On the sign-in page: **نسيت كلمة السر؟** sends a code to the account's e-mail (needs the mail
  server above). The new password signs every device out.

- In the panel (the manager): the account → **كلمة سر جديدة**. A new temporary password is
  shown once; the old one stops working and the account is unlocked.
- Five wrong passwords lock an account for 15 minutes. It opens by itself, or at once with
  **كلمة سر جديدة**.

## The manager is locked out or forgot the password (emergency)

```powershell
docker compose exec -it api dotnet /app/cli/Office.Cli.dll unlock-user --name owner
docker compose exec -it api dotnet /app/cli/Office.Cli.dll reset-password --name owner
docker compose exec -it api dotnet /app/cli/Office.Cli.dll reset-2fa --name owner
```

`reset-2fa` turns the authenticator off (a lost phone and no other manager to do it).

`reset-password` asks for the new password in the terminal and signs every session of that
account out. `list-users` shows every account (never a password or a hash).

## Someone leaves the office

**الحسابات → عطّل**. Every open session of that account ends at once. The account stays (for
the record of what they did) and can be enabled again. The last active manager cannot be
disabled or demoted.

## Alerts (TASK 07c)

The bell in the top bar shows the alerts a person may see: a task past its due time, an urgent
task, a client asking for someone. Each one also goes by e-mail (the no-reply sender) once, to the
manager, the lawyer and the staff member of the task (everyone, if it is unassigned). The e-mail
says only the kind of task and its due time, with a link to the panel. Tasks become overdue by
themselves every minute.

## The office's tools (TASK 07c; the manager and the lawyer)

- **الإعدادات → قوايم المكتب:** add or remove a number, or import a CSV (`name,phone[,staff]`) into
  the current clients, the lawyer's own list or the blocked. "Replace" swaps a whole list only
  when every line is valid. The manager also sees the muted conversations here and can give one
  back to Hazem.
- **الإعدادات → نصوص حازم:** change what Hazem says, word for word. Each change is a new version,
  used from the next message; «النسخ القديمة» brings an earlier one back. The limit of each text
  (300 or 600 characters) holds.
- **التقارير:** the month's tasks (by type, by staff member, done on time) and new clients (by
  source, topic and kind).
- **A client's file** (a picture, an ID, a contract): «افتح» on the task. Whoever may see the task
  may open it; every opening is recorded.

## What is recorded

`hazem.panel_audit`: sign-ins, failed attempts, account changes, task changes and assignments,
opened files, list changes, text versions and unmuting —
who, what and when. Never a password, never a client's words.
