# TASK 12 — تذكيرات المواعيد (DECISIONS v1.15, B-18 / M-03)

Branch `task12-reminders` from `main` (`8de9167`). Read DECISIONS v1.15 B-18, B-15 (changing a time), B-22
(refunds), B-08 (the hold, 1.13), M-02 / M-03 (the 24-hour window and the templates), the state machine of
`appointments`, and the TASK 08 / 09 / 11 reports first.

## Owner's decisions (the chat, «شوف الصح واعمله»: the proposals as written, DECISIONS v1.16)

| # | Proposal |
|---|---|
| 1 | Inside the 24-hour window: a normal message with buttons (the approved texts below). Outside it: the M-03 template (`reminder_day_before`, `reminder_2h_before`). Until Meta approves them, n8n refuses (`TEMPLATE_NOT_APPROVED`) and the office gets a task `reminder_failed` to remind the client by phone |
| 2 | «هحضر ✅» → recorded (`attendance = confirmed`), a short thanks; the panel and the calendar event show it |
| 3 | «تغيير أو إلغاء» → the existing step «تحب تغيّر الميعاد ولا تلغيه؟» (B-15 for a change: one free change 12 hours or more before; B-22 for a cancel). No new rules |
| 4 | No answer to the day-before reminder: nothing automatic (B-18). «المواعيد» shows «ما أكدش» for tomorrow's and how many are not confirmed (at the top of «الحجوزات»: the home page keeps the owner's four cards, and «مواعيد اليوم» there becomes a real number) |
| 5 | The details line: the office → the address and the map link (بيانات المكتب); phone / video → «الأستاذ هيكلم حضرتك على نفس الرقم ده في الميعاد»; Urfi → who must come (`UrfCase.Attendees()`) and «ومعاهم البطايق الأصل» |
| 6 | The lawyer's name in the reminders stays «الأستاذ سعد» as written in M-03 |
| 7 | The two reminder texts are the M-03 templates' bodies as submitted to Meta, quoted word for word in DECISIONS v1.16 B-18; the seven new ones were approved with the proposals. All active |

## What to build

1. **Migration 0017** (`database/migrations/0017_reminders.sql`):
   - `hazem.appointment_reminders`: appointment, `kind` (`day_before`, `two_hours`), `due_at` (after the rules),
     `state` (`scheduled`, `sent`, `failed`, `skipped`, `cancelled`), `sent_at`, `channel` (`message`, `template`),
     `skip_reason`. One per appointment and kind among the live ones.
   - `appointments.attendance` (`confirmed`, null) and `attendance_at`.
   - Scheduling in SQL, so every path is covered (a trigger on `appointments`): confirmed → both reminders;
     the time moved → the old ones `cancelled` and new ones made; cancelled / expired / completed / no_show →
     `cancelled`. Holds (`pending_payment`) never get these reminders (1.13).
   - Staff task type `reminder_failed`; audit target unchanged.
2. **.NET**:
   - `Office.Application/Reminders`: B-18's timing as a pure function (tested):
     a time between 00:00 and 10:00 Cairo → 10:00; if that leaves less than an hour before the appointment →
     skipped; a reminder already in the past when the booking is confirmed → skipped.
   - The worker (with the booking notices, every minute): due reminders → the message with its buttons, or the
     template when the window is closed (the button replies come back as `button.payload`, which Hazem reads already);
     a refusal → `failed` + task `reminder_failed`. Each is sent once (idempotency key `reminder:<id>`).
   - `HazemResponder`: the two buttons, before the AI, for this appointment only (an old reminder's button for a
     moved or cancelled appointment → «الميعاد ده اتغيّر/اتلغى» and the current one).
   - The calendar event gains «الحضور: أكّد ✅» when confirmed.
   - Panel API: each appointment carries its reminders and the attendance.
3. **Panel**: «المواعيد» shows the reminders (اتبعت / ما اتبعتش / فشل) and the attendance; the home count (4).
4. **Tests**: every B-18 rule (night → 10:00, under an hour → skipped, confirmed late → skipped, moved, cancelled,
   a hold gets none), the template when the window is closed, a refused template → the task, both buttons
   (B-15's free change and the 12 hours, a cancel within / after 24 hours), an old button, Urfi details, RBAC.
   Healthcheck, hazem-e2e step 15.
5. **Docs**: `panel-api.md`, `runbooks/booking.md` (reminders), `approved-texts-proposal.md`, the session report,
   DECISIONS v1.16 with the owner's answers.

## The texts (approved by the owner as proposed)

| Key | Text |
|---|---|
| `reminder_day_before` (M-03, active) | تذكير بميعاد حضرتك مع الأستاذ سعد بكرة {{day}} الساعة {{time}}. نوع الاستشارة: {{service}}. التفاصيل: {{details}}. من فضلك أكّد حضورك بالزرار تحت. |
| `reminder_2h_before` (M-03, active) | ميعاد حضرتك مع الأستاذ سعد النهارده الساعة {{time}}. نوع الاستشارة: {{service}}. التفاصيل: {{details}}. في انتظار حضرتك. |
| `btn_reminder_attend` | هحضر ✅ |
| `btn_reminder_change` | تغيير أو إلغاء |
| `reminder_attend_ok` | تمام ✅ في انتظار حضرتك {{day}} الساعة {{time}}. |
| `reminder_details_office` | المكتب في {{address}}، والخريطة: {{map}} |
| `reminder_details_phone` | الأستاذ هيكلم حضرتك على نفس الرقم ده في الميعاد |
| `reminder_details_urf` | لازم يحضر {{attendees}}، ومعاهم البطايق الأصل. المكتب في {{address}} |
| `reminder_old_button` | الميعاد ده اتغيّر أو اتلغى. ميعاد حضرتك دلوقتي {{day}} الساعة {{time}}. |

## Not in this task

Real Meta and submitting the templates (`META-WA-TEMPLATES-SUBMIT` once Meta is set up), reminders for the
written services (no appointment), marking `completed` / `no_show` (already in the panel), the urgent path
(still waiting for the lawyer).
