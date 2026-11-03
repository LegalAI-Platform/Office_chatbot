# TASK 19 — A passed appointment is marked «خلص» by itself when nobody marked it (DECISIONS v1.50)

**Status: a plan for the owner to review. Nothing is built.** Branch from `main` (v1.49). The owner (2026-11-03): «"خلص"
تتعلّم لوحدها لو عدّى على الميعاد وقت معيّن… ويبقى ممكن يتعدّل بعدها لو العميل ماحضرش»، and «طب ولو الراجل غير ميعاد
الاستشاره؟».

## 1. Why
- Today `completed` / `no_show` is set only by a person in the panel («المواعيد» → «خلص» / «ماحضرش», `PanelBooking.MarkAsync`,
  from `confirmed` only, one-way).
- If nobody marks it:
  - the appointment stays `confirmed` for good;
  - the rating never goes (B-26: it waits for `completed_at` + 2 hours);
  - Hazem can treat the client as still having an appointment to come. On ***89 (2026-11-01), a passed 17:30 call still
    held the request open, so a new «السلام عليكم» got no welcome.

## 2. The rule
1. **What is marked:**
   - a `confirmed` appointment whose `ends_at` is more than **N hours** ago (`booking.auto_complete_hours`, panel setting,
     default **3**, `0` = off);
   - nobody has marked it;
   - **its payment was verified by the office.**
   - Every kind: phone, video, office, urgent, Urfi.
2. **Never marked:**
   - `pending_payment`, `expired` or cancelled appointments;
   - a `confirmed` one whose transfer is still under review: it gets an office task instead, once: «ميعاد عدّى ومحدش علّمه،
     والتحويل لسه ماتراجعش».
3. **A rescheduled appointment:** a move (by Hazem or the panel) moves the same row's `starts_at` / `ends_at`, so the N hours
   count from the new time. Nothing special is needed. A cancellation leaves it cancelled, never marked.
4. **Who marked it:** a new column `completed_by` (`staff` / `auto`). The panel shows «خلص (تلقائي)» in its own colour.
5. **Correcting an automatic mark:**
   - From the panel, an automatic `completed` can still become `no_show`, or go back to `confirmed` and be moved.
   - Allowed until its rating has gone out. After that it stays, with the audit.
   - A staff mark stays one-way, as today.
6. **The rating after an automatic mark** goes at the next **10:00** (and at least 2 hours after the mark), not after 2 hours.
   This gives the office a morning to correct it. A staff mark keeps B-26's 2 hours.
7. **The rule for the office**, written in the runbook: a time changed by phone is changed in the panel too, or Hazem sends a
   rating for a consultation that did not happen.

## 3. The build
- **Migration `0051`:**
  - `appointments.completed_by text CHECK (completed_by IN ('staff','auto'))`;
  - the setting `booking.auto_complete_hours` (3);
  - an SQL function `hazem.auto_complete_appointments(now)` that marks the due rows and returns them, atomic and safe to run
    twice;
  - a task type for «transfer not reviewed, appointment passed».
- **Worker:** the booking worker (`CalendarWorker`, with the reminders and ratings) calls the function once a minute.
- **`PanelBooking.MarkAsync`:**
  - sets `completed_by = 'staff'`;
  - a new correction path for `completed_by = 'auto'` with no rating sent: to `no_show` or back to `confirmed`, with the
    audit (`appointment.auto_corrected`).
- **`RatingNotices`:** the due time of a rating after an automatic mark (rule 6).
- **Panel («المواعيد»):**
  - the «خلص (تلقائي)» badge;
  - the «ماحضرش» / «رجّعه» buttons on it while allowed;
  - the setting in «الإعدادات» with its explanation.
- **Hazem:** nothing changes in the replies. A completed appointment is no longer an open item (B-28 already), so the welcome
  comes back by itself.

## 4. Done when
- Unit and integration tests:
  - marked after N hours;
  - not before;
  - not unverified (task once);
  - not cancelled or unpaid;
  - a moved appointment counts from its new time;
  - `0` turns it off;
  - correcting an automatic mark;
  - the rating's time;
  - a staff mark unchanged.
- `backend-test`, `web-test`, `healthcheck`. Then `quality-eval` on the returning-client conversations: nothing drops.
- DECISIONS v1.50 (S-03, B-26, B-28) and the session report.

## 5. Decisions for the owner (recommendation first)
1. **N = 3 hours** after the appointment's end (the panel can change it).
2. **Verified payment only**; an unverified one gets a task instead of the mark.
3. **The rating after an automatic mark at the next 10:00**, so the office has a morning to correct it.
4. **Correctable until the rating is sent.**
