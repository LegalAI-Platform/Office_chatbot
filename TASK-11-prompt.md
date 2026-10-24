# TASK 11 — العرفي (DECISIONS v1.15, B-11 / S-06)

Branch `task11-urfi` from `main` (`d20dd62`). Read DECISIONS v1.15 B-11 (with its 1.15 block), S-06, B-08 (the hold),
B-22 (refunds), and the TASK 09 / 10 reports first.

## Owner's decisions (the chat, «موافق على اقتراحاتك في تاسك 11»)

| # | Decision |
|---|---|
| 1 | Urfi appointment length: a panel setting `booking.urf_minutes`, default **60**. In the office only |
| 2 | The closed questions are **buttons / a list**, read without the AI |
| 3 | The ages are typed («30 و 25») and **read by code**; unclear → asked again with numbers; twice → a task |
| 4 | The time is held right after the questions (B-08 hold); the documents come before the appointment; a task when 24 hours are left without them |
| 5 | New texts are proposals (inactive) until approved; texts quoted word for word in DECISIONS are active |

## What to build

1. **Migration 0016** (`database/migrations/0016_urf_cases.sql`):
   - `hazem.urf_cases` (S-06): person, intake, `kind` (marriage / divorce), `state`, `step` (the question waiting), the
     answers (ages, the wife's past: first / divorced / khul, guardian, witnesses, has copy, agreed, children), `age_tries`,
     `appointment_id`, `policy_version` per decision, who decided and when, `client_notice` / `notice_kind`
     (approved, rejected), `docs_task_at`. One open case per client.
   - `hazem.urf_files` (where a document is, never its content).
   - `appointments.service` gains `urf_marriage_office`, `urf_marriage_own`, `urf_divorce_office`, `urf_divorce_own`.
   - `hazem.file_target` gains `urf_documents` (after the receipts, before the service orders); `hazem.add_urf_file`.
   - `hazem.review_urf_case(case, decision, by, policy)`: approved → the confirmation for the worker; rejected → the
     appointment `cancelled_by_office` (no B-22 list), the calendar event removed, `request_refund(…, 'office_cancelled')`.
   - Setting `booking.urf_minutes` = 60; staff task types `urf_review`, `urf_documents_missing`, `urf_help`; audit
     target `urf`; texts (below).
2. **.NET**:
   - `Office.Application/Urf`: the questions as a pure state machine (tested), `IUrfStore`, `UrfDesk`.
   - `BookingService`: the four Urfi services (5,000 / 4,000, office), their minutes from the setting.
   - `HazemResponder`: Urfi on = booking on + every Urfi text active. «عايز جواز/طلاق عرفي» starts or resumes the case;
     buttons and the typed ages are answered before the AI; after the witnesses → the price, the documents and the
     free times; the pick → the hold, the case `booked`; the receipt → «ابعت صور المستندات»; the documents → the case
     (never the AI), the first one → «استلمت» + task `urf_review`. Divorce without a copy / not agreed / children →
     the consultation offer (`redirected_consultation`). Under 18 / no guardian → the approved apology, closed.
   - The worker: the approval / rejection messages; the 24-hour documents task.
   - Panel API `/api/panel/urf` (list, files, approve, reject); permissions `urf.manage` (all), `urf.review`
     (lawyer, manager); the booking settings carry `urf_minutes`.
3. **Panel**: «حالات العرفي» (answers, documents, the appointment and its payment, approve / reject with a confirmation);
   the booking settings field.
4. **Tests**: every row of B-11's table (17, the brother, the uncle, no guardian, khul, «عرفي» alone, divorce at once,
   divorce with a child / not agreed / no copy), the ages parser, the booking with the Urfi minutes and price, the
   documents never to the AI, approve and reject (refund, calendar), the 24-hour task, RBAC. Healthcheck, hazem-e2e step 14.
5. **Docs**: `panel-api.md`, `runbooks/urf.md`, `approved-texts-proposal.md`, the session report.

## Not in this task

Real Meta, the WhatsApp templates (the 24-hour window), encryption at rest in Garage (test data only; R2 encrypts in
production), the urgent path (still waiting for the lawyer).
