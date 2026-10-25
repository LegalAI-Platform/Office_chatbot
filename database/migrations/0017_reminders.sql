-- 0017_reminders: TASK 12 (DECISIONS v1.16, B-18 / M-03): the reminders before a confirmed appointment.
--   hazem.appointment_reminders  one row per confirmed appointment, kind and the time it is for: the day before (with
--                     «هحضر ✅» and «تغيير أو إلغاء») and two hours before. The API's worker makes them with B-18's
--                     timing (a time between midnight and 10:00 moves to 10:00; less than an hour before the
--                     appointment, or already past when the booking was confirmed: skipped), sends each once (a normal
--                     message inside the 24-hour window, the M-03 template outside it), and cancels the ones of an
--                     appointment that moved, was cancelled or ended. A hold (pending_payment) never gets them (1.13).
--   appointments.attendance_at / attendance_for  the client pressed «هحضر ✅», and for which time (a moved
--                     appointment is not confirmed again until he presses again).
--   A reminder that could not be sent (no approved template yet, the office's address missing) is a task
--   (reminder_failed): the office reminds the client by phone.

-- migrate:up
CREATE TABLE hazem.appointment_reminders (
  id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  appointment_id uuid NOT NULL REFERENCES hazem.appointments (id) ON DELETE CASCADE,
  kind           text NOT NULL CHECK (kind IN ('day_before', 'two_hours')),
  starts_at      timestamptz NOT NULL,  -- the appointment's time this reminder is for
  due_at         timestamptz,           -- after B-18's rules; null when it was never due (skipped at once)
  -- sending: claimed by the worker right before it goes (a send cut off half way is tried again after 5 minutes,
  -- under the same idempotency key, so n8n never sends it twice).
  state          text NOT NULL DEFAULT 'scheduled' CHECK (state IN ('scheduled', 'sending', 'sent', 'failed', 'skipped', 'cancelled')),
  -- too_close: under an hour before (B-18); passed: its time had gone when the booking was confirmed or moved;
  -- late: the worker reached it when «بكرة» / «النهارده» or the hour before were no longer true; off: the texts are not approved.
  skip_reason    text CHECK (skip_reason IN ('too_close', 'passed', 'late', 'off')),
  channel        text CHECK (channel IN ('message', 'template')),
  error_code     text CHECK (length(error_code) <= 80),
  sent_at        timestamptz,
  created_at     timestamptz NOT NULL DEFAULT now(),
  updated_at     timestamptz NOT NULL DEFAULT now(),
  UNIQUE (appointment_id, kind, starts_at),
  CHECK ((state = 'skipped') = (skip_reason IS NOT NULL)),
  CHECK (state NOT IN ('scheduled', 'sending') OR due_at IS NOT NULL),
  CHECK (state <> 'sent' OR (channel IS NOT NULL AND sent_at IS NOT NULL))
);
CREATE INDEX appointment_reminders_due_idx ON hazem.appointment_reminders (due_at) WHERE state = 'scheduled';
CREATE TRIGGER trg_appointment_reminders_updated_at BEFORE UPDATE ON hazem.appointment_reminders
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

ALTER TABLE hazem.appointments
  ADD COLUMN attendance_at  timestamptz,
  ADD COLUMN attendance_for timestamptz;

ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help',
  'reminder_failed'));

-- The texts. The two reminders are the M-03 templates' bodies as submitted to Meta (n8n/templates, TASK 02), so a
-- client reads the same words inside and outside the 24-hour window; DECISIONS v1.16 B-18 quotes them word for word. The rest were proposed in TASK 12 and approved
-- by the owner as proposed (2026-10-25, «شوف الصح واعمله»). Without every one of them no reminder is sent.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('reminder_day_before', 1, 'تذكير بميعاد حضرتك مع الأستاذ سعد بكرة {{day}} الساعة {{time}}. نوع الاستشارة: {{service}}. التفاصيل: {{details}}. من فضلك أكّد حضورك بالزرار تحت.', 600, true, 'DECISIONS v1.16 B-18', 'migration 0017'),
  ('reminder_2h_before', 1, 'ميعاد حضرتك مع الأستاذ سعد النهارده الساعة {{time}}. نوع الاستشارة: {{service}}. التفاصيل: {{details}}. في انتظار حضرتك.', 600, true, 'DECISIONS v1.16 B-18', 'migration 0017'),
  ('btn_reminder_attend', 1, 'هحضر ✅', 20, true, 'proposal TASK 12', 'owner approval 2026-10-25'),
  ('btn_reminder_change', 1, 'تغيير أو إلغاء', 20, true, 'proposal TASK 12', 'owner approval 2026-10-25'),
  ('reminder_attend_ok', 1, 'تمام ✅ في انتظار حضرتك {{day}} الساعة {{time}}.', 300, true, 'proposal TASK 12', 'owner approval 2026-10-25'),
  ('reminder_details_office', 1, 'المكتب في {{address}}، واللوكيشن: {{map}}', 300, true, 'proposal TASK 12', 'owner approval 2026-10-25'),
  ('reminder_details_phone', 1, 'الأستاذ هيكلم حضرتك على نفس الرقم ده في الميعاد', 300, true, 'proposal TASK 12', 'owner approval 2026-10-25'),
  ('reminder_details_urf', 1, 'لازم يحضر {{attendees}} ومعاهم البطايق الأصل، والمكتب في {{address}}، واللوكيشن: {{map}}', 300, true, 'proposal TASK 12', 'owner approval 2026-10-25'),
  ('reminder_old_button', 1, 'الميعاد ده اتغيّر. ميعاد حضرتك دلوقتي {{day}} الساعة {{time}}.', 300, true, 'proposal TASK 12', 'owner approval 2026-10-25')
ON CONFLICT (key, version) DO NOTHING;
-- Applied again after a rollback: a text a reply kept (the rollback switched it off) comes back on, unless another
-- version of it is active (the office reworded it in «نصوص حازم»).
UPDATE hazem.approved_texts t SET active = true
 WHERE t.source IN ('proposal TASK 12', 'DECISIONS v1.16 B-18') AND t.version = 1 AND NOT t.active
   AND NOT EXISTS (SELECT 1 FROM hazem.approved_texts a WHERE a.key = t.key AND a.active);

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.appointment_reminders TO office_api;
GRANT USAGE, SELECT ON SEQUENCE hazem.appointment_reminders_id_seq TO office_api;
GRANT SELECT ON hazem.appointment_reminders TO office_readonly;
REVOKE ALL ON hazem.appointment_reminders FROM office_app;

-- migrate:down
UPDATE hazem.approved_texts SET active = false WHERE source IN ('proposal TASK 12', 'DECISIONS v1.16 B-18');
DELETE FROM hazem.approved_texts t
 WHERE t.source IN ('proposal TASK 12', 'DECISIONS v1.16 B-18')
   AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);

DELETE FROM hazem.staff_tasks WHERE type = 'reminder_failed';
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help'));

ALTER TABLE hazem.appointments DROP COLUMN IF EXISTS attendance_for, DROP COLUMN IF EXISTS attendance_at;
DROP TABLE IF EXISTS hazem.appointment_reminders;
