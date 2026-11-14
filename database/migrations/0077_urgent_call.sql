-- v1.110 (the owner, 2026-11-14: «هو دلوقتي الاستشاره العاجله بتروح ل كام ايميل ؟؟؟» … «انا حاسس كميه ايميلات كتيره اوي» …
-- «موافق»). One paid urgent consultation made 3 tasks and 9 to 12 e-mails on the test server (urgent_request,
-- urgent_call_failed, urgent_call_unanswered, each to the lawyers and the staff, then «late» for each). Now it is one task:
--   urgent_call              the task the payment opens (instead of urgent_request on that path). Its alert is the bell's
--                            and Windows' only: the lawyer's WhatsApp alert is his.
--   urgent_call_failed       the same task, when that WhatsApp alert did not reach him: one e-mail, to the lawyer.
--   urgent_call_unanswered   the same task, when nobody pressed «هكلمه» in time: one e-mail, to the staff.
-- No «late» e-mail for any of them (the escalation is that). The code moves the task's type; this only allows the new one.
-- (v1.108's own 0077, booking_tasks, was taken out of main with it (#153) and never ran on the server: if it comes back, it
-- comes back as 0078.)

-- migrate:up
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help',
  'reminder_failed', 'office_info_missing',
  'service_question', 'payment_review_due',
  'rating_low', 'urgent_call_failed', 'urgent_call_unanswered',
  'appointment_unmarked', 'urgent_call'));

-- migrate:down
UPDATE hazem.staff_tasks SET type = 'urgent_request' WHERE type = 'urgent_call';
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help',
  'reminder_failed', 'office_info_missing',
  'service_question', 'payment_review_due',
  'rating_low', 'urgent_call_failed', 'urgent_call_unanswered',
  'appointment_unmarked'));
