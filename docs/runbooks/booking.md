# Runbook: the booking (TASK 08, DECISIONS v1.12; TASK 09, v1.13)

Hazem offers free times on WhatsApp. **(v1.13) A picked time is held for the client until his receipt
image** (two hours, one when the appointment is soon, always ending 15 minutes before it: «إعدادات الحجز»);
the image confirms it, and a hold that runs out frees the time again. The payments, their review and the
refunds: [payments.md](payments.md). A free time is **inside the hours of
the panel** and **clear of every event in the lawyer's Google Calendar** (B-24, both together).
Everything about the hours is changed from the panel, by the manager, the lawyer or any staff
member, and Hazem uses the change at once.

## Changing the hours (the panel → «المواعيد»)

| What | Where |
|---|---|
| Close one day (the lawyer is busy on Monday) | **التقويم** → the day → «اقفل اليوم كله» → احفظ |
| Close some hours of a day | **التقويم** → the day → «اقفل جزء منه» → من / لـ → احفظ |
| Other hours for one day only | **التقويم** → the day → «ساعات مختلفة لليوم ده» → احفظ |
| The hours of every week | **المواعيد الثابتة**: each day one or more ranges, or none (إجازة) → احفظ الأسبوع |
| Buffer, notice, furthest day, a daily limit, every how many minutes, how many times Hazem offers | **إعدادات الحجز** |
| Switch booking from WhatsApp on or off | **إعدادات الحجز** → «حازم يحجز من واتساب» |
| InstaPay, the wallet, the address, the map, **(1.25)** the Google Meet link and the office's phone (manager and lawyer only) | **بيانات المكتب** |

- Every day shows **«حازم هيعرض إيه»**: the exact times Hazem would offer that day, now.
- **«تراجع»** (top of the page) puts back the last change. Appointments already cancelled stay
  cancelled. An undo (or removing an exception) that would leave a booking outside the hours asks
  first, like any other change.
- **A change that leaves confirmed appointments outside the hours asks first** and lists them. If
  you go on, they are cancelled by the office and Hazem writes to each client: «الأستاذ عنده ظرف
  في الميعاد ده، تحب ميعاد تاني ولا استرداد كامل؟» with the nearest free times (B-22), within
  about 10 seconds. A client Hazem could not reach becomes a task (`booking_message_failed`). With
  «حازم يحجز من واتساب» off, the client gets the text alone and the office a task
  (`office_cancelled`) to offer another time or the refund.
- **الحجوزات**: the next 30 days; mark «خلص» or «ماحضرش», or cancel by the office.

## Connecting the lawyer's Google Calendar (the owner, once)

Hazem never sees the calendar's other details: it asks only which times are busy, and adds one
event per booking («الخدمة – الاسم – الرقم – نوع الموضوع – إيصال»), never the client's words.

1. https://console.cloud.google.com → a project (e.g. «hazem») → **APIs & Services → Library** →
   **Google Calendar API** → Enable.
2. **IAM & Admin → Service Accounts → Create service account** (e.g. `hazem-calendar`), no role.
3. The account → **Keys → Add key → Create new key → JSON**. A file downloads. Keep it private.
4. Put it in `.env` as ONE line of base64 (PowerShell, in the folder of the file):
   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("hazem-calendar.json")) | Set-Clipboard
   ```
   then paste after `GOOGLE_SERVICE_ACCOUNT_JSON=` in `.env` yourself, and delete the JSON file.
   Never send it in a chat.
5. The lawyer opens Google Calendar → his calendar → **Settings and sharing → Share with specific
   people** → the service account's e-mail (`…@….iam.gserviceaccount.com`) → **Make changes to
   events**.
6. `docker compose up -d api`, then in the panel → **إعدادات الحجز**: the calendar's e-mail (the
   lawyer's Gmail) → احفظ. The page says what is still missing.
7. Write the office's details (**بيانات المكتب**), then switch **«حازم يحجز من واتساب»** on. (The
   booking's texts were approved on 2026-10-24; any change to them is in **نصوص حازم**.)

Check the connection at any time (it prints the service account's e-mail, never the key):

```powershell
docker compose exec api dotnet /app/cli/Office.Cli.dll calendar-check
docker compose exec api dotnet /app/cli/Office.Cli.dll calendar-check --calendar someone@gmail.com
docker compose exec api dotnet /app/cli/Office.Cli.dll calendar-check --write   # also writes: one test event, deleted at once
```

`"ok": true` = the API reads that calendar's busy times. Otherwise `problem` says what is missing
(the key, the calendar id, the sharing, or the Calendar API not enabled in the project).

If the calendar does not answer, Hazem offers no time (it could be busy) and the office gets a task
(`booking_unavailable`). An event the lawyer adds over a booking makes a task (`calendar_conflict`);
the client is not written to by itself (`booking.conflict_auto_message` = false, P3).

## The reminders (TASK 12, DECISIONS v1.16 B-18)

The API's worker (every 10 seconds, with the booking's notices) gives every **confirmed** appointment two reminders:
the day before at the same hour, with «هحضر ✅» and «تغيير أو إلغاء», and two hours before. A time between midnight and
10:00 waits for 10:00; one that would then be under an hour before, or that had passed when the booking was confirmed,
is skipped. A hold (`pending_payment`) gets none until its receipt confirms it. A moved appointment gets new ones; a
cancelled one loses its own.

- **Inside the 24-hour window** (the client wrote in the last day) the reminder is a normal message. **Outside it** the
  M-03 template goes (`reminder_day_before` / `reminder_2h_before`, the same words and buttons): it needs **Meta's
  approval** first (`META-WA-TEMPLATES-SUBMIT`, after the Meta setup, `docs/meta-setup.md`).
- A reminder that cannot go (the template not approved yet, or an office appointment while بيانات المكتب has no address
  or map) is **never sent half-written**: the office gets a task «تذكير ما وصلش: فكّر العميل بالتليفون» (`reminder_failed`).
- «هحضر ✅» is recorded for the appointment's time («أكّد حضوره ✅» in «المواعيد», «الحضور: ✅ أكّد» in the calendar
  event). «تغيير أو إلغاء» asks «تحب تغيّر الميعاد ولا تلغيه؟» (B-15 and B-22 as they are); with the booking from WhatsApp
  off it becomes a task. Nobody is cancelled for not answering.
- «المواعيد» → «الحجوزات» shows each reminder (هيتبعت إمتى / اتبعت / ما وصلش / ما اتبعتش وليه) and, at the top, how many
  of tomorrow's appointments are not confirmed yet.
- The reminders run while their 9 texts are active («نصوص حازم»); with one switched off, no reminder goes («ما اتبعتش:
  نصوص التذكير مش متفعّلة»).

## Checks

`healthcheck` («The booking» section): the tables, the no-overlap constraint, n8n locked out, no
test stub or test day left, no calendar event stuck; WARN while `GOOGLE_SERVICE_ACCOUNT_JSON` is
empty. `hazem-e2e` step 11 books through the Google Calendar stub and closes days from the panel.
«The reminders» section: the table, n8n locked out, no reminder more than 10 minutes past its time (the worker
stopped), the 9 texts active; WARN while a `reminder_failed` task is open. `hazem-e2e` step 15 sends a day-before
reminder through n8n and presses «هحضر ✅».
