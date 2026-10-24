-- 0013_booking: TASK 08 (DECISIONS v1.12, B-08 / B-09 / B-15 / B-22 / B-24 / S-03).
--   hazem.booking_weekly_hours  the default hours of each weekday (minutes of the Cairo day, 0–1440);
--                               a day with no row is a day off. Edited from the panel.
--   hazem.booking_exceptions    one date: closed all day, some hours closed, or other hours that day.
--   hazem.booking_changes       every change from the panel (before / after), for the record and «undo».
--   hazem.appointments          S-03. Two confirmed appointments can never overlap (an EXCLUDE
--                               constraint, so even two clients in the same second cannot both win).
--                               calendar_state: what the worker still has to do in Google Calendar.
--   hazem.book_appointment / hazem.move_appointment: B-08.3 and B-15, atomic (one lock, the
--                               buffer, one open booking per client and service, idempotent per turn).
-- Settings: booking.* (the panel's booking settings; booking.enabled stays false until the owner
-- connects Google Calendar) and office.* (P1: InstaPay, wallet, address, written from the panel).

-- migrate:up
CREATE TABLE hazem.booking_weekly_hours (
  weekday      smallint NOT NULL CHECK (weekday BETWEEN 0 AND 6), -- 0 = Sunday … 6 = Saturday
  start_minute int NOT NULL CHECK (start_minute BETWEEN 0 AND 1439),
  end_minute   int NOT NULL CHECK (end_minute BETWEEN 1 AND 1440),
  CHECK (end_minute > start_minute),
  PRIMARY KEY (weekday, start_minute)
);
-- B-24 (1.4): every day 10:00 to midnight, until the office changes it.
INSERT INTO hazem.booking_weekly_hours (weekday, start_minute, end_minute)
SELECT d, 600, 1440 FROM generate_series(0, 6) AS d;

CREATE TABLE hazem.booking_exceptions (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  day          date NOT NULL,
  kind         text NOT NULL CHECK (kind IN ('closed_day', 'closed_hours', 'custom_hours')),
  start_minute int CHECK (start_minute BETWEEN 0 AND 1439),
  end_minute   int CHECK (end_minute BETWEEN 1 AND 1440),
  reason       text CHECK (char_length(reason) <= 200),
  created_by   uuid REFERENCES hazem.panel_users (id) ON DELETE SET NULL,
  created_at   timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT booking_exceptions_interval CHECK (
    (kind = 'closed_day' AND start_minute IS NULL AND end_minute IS NULL)
    OR (kind <> 'closed_day' AND start_minute IS NOT NULL AND end_minute IS NOT NULL AND end_minute > start_minute))
);
CREATE INDEX booking_exceptions_day_idx ON hazem.booking_exceptions (day);

CREATE TABLE hazem.booking_changes (
  id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  at        timestamptz NOT NULL DEFAULT now(),
  user_id   uuid REFERENCES hazem.panel_users (id) ON DELETE SET NULL,
  what      text NOT NULL CHECK (what IN ('weekly', 'exception_add', 'exception_remove', 'settings', 'office')),
  before    jsonb,
  after     jsonb,
  undone_at timestamptz
);
CREATE INDEX booking_changes_at_idx ON hazem.booking_changes (at DESC);

CREATE TABLE hazem.appointments (
  id                uuid PRIMARY KEY,
  person_id         uuid NOT NULL REFERENCES hazem.persons (id),
  intake_id         uuid,
  service           text NOT NULL CHECK (service IN ('consultation_phone_video', 'consultation_office', 'consultation_urgent')),
  starts_at         timestamptz NOT NULL,
  ends_at           timestamptz NOT NULL,
  state             text NOT NULL DEFAULT 'confirmed'
                      CHECK (state IN ('confirmed', 'completed', 'no_show', 'cancelled_by_client', 'cancelled_by_office')),
  amount            int NOT NULL CHECK (amount >= 0),
  reschedules       int NOT NULL DEFAULT 0 CHECK (reschedules >= 0),
  matter_type       text,
  booked_turn_id    text NOT NULL UNIQUE,
  last_move_turn_id text,
  calendar_event_id text,
  calendar_state    text NOT NULL DEFAULT 'create'
                      CHECK (calendar_state IN ('create', 'update', 'remove', 'synced', 'removed')),
  calendar_attempts int NOT NULL DEFAULT 0,
  calendar_next_at  timestamptz NOT NULL DEFAULT now(),
  conflict_noted_at timestamptz,
  created_at        timestamptz NOT NULL DEFAULT now(),
  updated_at        timestamptz NOT NULL DEFAULT now(),
  CHECK (ends_at > starts_at),
  CONSTRAINT appointments_no_overlap EXCLUDE USING gist (tstzrange(starts_at, ends_at) WITH &&) WHERE (state = 'confirmed')
);
CREATE INDEX appointments_starts_idx ON hazem.appointments (starts_at);
CREATE INDEX appointments_person_idx ON hazem.appointments (person_id, state);
CREATE INDEX appointments_calendar_idx ON hazem.appointments (calendar_next_at) WHERE calendar_state IN ('create', 'update', 'remove');
CREATE TRIGGER trg_appointments_updated_at BEFORE UPDATE ON hazem.appointments
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- B-08.3: confirm at once, or say why not. The API checked Google Calendar for this slot just before.
CREATE FUNCTION hazem.book_appointment(
  p_id uuid, p_person uuid, p_intake uuid, p_service text, p_start timestamptz, p_end timestamptz,
  p_amount int, p_turn text, p_buffer_minutes int, p_matter text)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_id uuid;
  v_buffer interval := make_interval(mins => GREATEST(p_buffer_minutes, 0));
BEGIN
  SELECT id INTO v_id FROM hazem.appointments WHERE booked_turn_id = p_turn;
  IF FOUND THEN
    RETURN jsonb_build_object('status', 'exists', 'id', v_id); -- the same turn again (a retry)
  END IF;

  PERFORM pg_advisory_xact_lock(hashtext('hazem.appointments'));

  SELECT id INTO v_id FROM hazem.appointments
   WHERE person_id = p_person AND service = p_service AND state = 'confirmed' AND ends_at > now()
   LIMIT 1;
  IF FOUND THEN
    RETURN jsonb_build_object('status', 'already_booked', 'id', v_id); -- B-08: one open booking per service
  END IF;

  IF EXISTS (SELECT 1 FROM hazem.appointments
              WHERE state = 'confirmed'
                AND tstzrange(starts_at - v_buffer, ends_at + v_buffer) && tstzrange(p_start, p_end)) THEN
    RETURN jsonb_build_object('status', 'taken');
  END IF;

  INSERT INTO hazem.appointments (id, person_id, intake_id, service, starts_at, ends_at, amount, matter_type, booked_turn_id)
  VALUES (p_id, p_person, p_intake, p_service, p_start, p_end, p_amount, p_matter, p_turn);
  RETURN jsonb_build_object('status', 'booked', 'id', p_id);
END $$;

-- B-15: the one free change. The API decided it is allowed (12 hours before); this keeps it atomic.
CREATE FUNCTION hazem.move_appointment(
  p_id uuid, p_start timestamptz, p_end timestamptz, p_turn text, p_buffer_minutes int, p_max_moves int)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.appointments;
  v_buffer interval := make_interval(mins => GREATEST(p_buffer_minutes, 0));
BEGIN
  PERFORM pg_advisory_xact_lock(hashtext('hazem.appointments'));
  SELECT * INTO v FROM hazem.appointments WHERE id = p_id FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v.last_move_turn_id = p_turn THEN
    RETURN jsonb_build_object('status', 'exists'); -- the same turn again
  END IF;
  IF v.state <> 'confirmed' OR v.reschedules >= p_max_moves THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  IF EXISTS (SELECT 1 FROM hazem.appointments
              WHERE id <> p_id AND state = 'confirmed'
                AND tstzrange(starts_at - v_buffer, ends_at + v_buffer) && tstzrange(p_start, p_end)) THEN
    RETURN jsonb_build_object('status', 'taken');
  END IF;

  UPDATE hazem.appointments
     SET starts_at = p_start, ends_at = p_end, reschedules = reschedules + 1, last_move_turn_id = p_turn,
         calendar_state = CASE WHEN calendar_event_id IS NULL THEN 'create' ELSE 'update' END,
         calendar_attempts = 0, calendar_next_at = now(), conflict_noted_at = NULL
   WHERE id = p_id;
  RETURN jsonb_build_object('status', 'moved');
END $$;

-- Tasks from the booking (documented in the TASK 08 report).
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict'));

ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation', 'booking', 'appointment'));

INSERT INTO hazem.settings (key, value, description) VALUES
  ('booking.enabled', 'false', 'true = Hazem offers free times and books (TASK 08). Turn on after Google Calendar is connected (docs/runbooks/booking.md)'),
  ('booking.buffer_minutes', '10', 'B-24: minutes kept free between two appointments or events (the panel)'),
  ('booking.min_notice_hours', '2', 'B-24: the earliest a normal appointment may start, in hours from now (not the urgent one)'),
  ('booking.horizon_days', '14', 'B-24: the latest day that may be booked, in days from today'),
  ('booking.max_per_day', '', 'B-24: at most this many appointments a day; empty = no limit'),
  ('booking.slot_step_minutes', '15', 'P5: appointments start every this many minutes (10:00, 10:15, …)'),
  ('booking.offer_count', '4', 'B-08: how many free times Hazem offers at once'),
  ('booking.calendar_id', '', 'T-14: the lawyer''s Google Calendar id (his e-mail), shared with the service account'),
  ('booking.google_base_url', '', 'Tests only (ENVIRONMENT=test): the Google Calendar stub. Must stay empty'),
  ('booking.conflict_auto_message', 'false', 'P3: true = a calendar event over a booking writes to the client by itself (B-22); false = an alert and a task only'),
  ('office.instapay', '', 'P1: the office''s InstaPay address, written from the panel'),
  ('office.wallet_number', '', 'P1: the office''s wallet number, written from the panel'),
  ('office.wallet_provider', '', 'P1: the wallet''s provider (e.g. Vodafone Cash), written from the panel'),
  ('office.address', '', 'P1: the office''s address, written from the panel'),
  ('office.map_url', '', 'P1: the office''s Google Maps link, written from the panel')
ON CONFLICT (key) DO NOTHING;

-- B-21 limits a message (300, or 600 for payment details); a WhatsApp button title is 20 characters
-- and a list row's 24 (Meta's limits), so the titles of the booking's buttons carry those.
ALTER TABLE hazem.approved_texts DROP CONSTRAINT approved_texts_max_chars_check;
ALTER TABLE hazem.approved_texts ADD CONSTRAINT approved_texts_max_chars_check CHECK (max_chars IN (20, 24, 300, 600));

-- From DECISIONS, word for word, with the system's slots (active). B-21: the payment text may be 600.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('booking_confirmed', 1, '✅ تم حجز {{service}} يوم {{day}} الساعة {{time}}. المبلغ {{amount}} جنيه، مقدمًا على InstaPay: {{instapay}}، أو محفظة كاش: {{wallet}}. بعد التحويل ابعتلي صورة الإيصال هنا. الاسترداد كامل لو الإلغاء خلال 24 ساعة من الدفع وقبل بداية الخدمة.', 600, true, 'DECISIONS v1.4 B-08', 'migration 0013'),
  ('booking_cancelled_by_office', 1, 'الأستاذ عنده ظرف في الميعاد ده، تحب ميعاد تاني ولا استرداد كامل؟', 300, true, 'DECISIONS v1.4 B-22', 'migration 0013')
ON CONFLICT (key, version) DO UPDATE SET active = true;

-- Proposed in TASK 08 and approved by the owner as proposed (2026-10-24, in chat): active.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('btn_book_phone_video', 1, '📞 تليفون أو فيديو', 20, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('btn_book_office', 1, '🏢 مقابلة في المكتب', 20, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('booking_choose_slot', 1, 'اختار الميعاد اللي يناسبك من القايمة 👇', 300, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('btn_slots_list', 1, 'المواعيد', 20, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('btn_more_slots', 1, 'مواعيد تانية', 24, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('booking_slot_taken', 1, 'الميعاد ده لسه اتحجز. اختار ميعاد تاني من القايمة 👇', 300, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('booking_no_slots', 1, 'مفيش مواعيد فاضية دلوقتي. بلّغت المكتب بطلب حضرتك.', 300, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('booking_unavailable', 1, 'مش قادر أشوف المواعيد الفاضية دلوقتي. بلّغت المكتب بطلب حضرتك.', 300, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('booking_urgent_none_today', 1, 'مفيش مواعيد فاضية النهارده. دي أقرب المواعيد بعد كده 👇', 300, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('booking_existing', 1, 'حضرتك حاجز {{service}} يوم {{day}} الساعة {{time}}. تحب تغيّر الميعاد ولا تلغيه؟', 300, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('btn_reschedule', 1, '🔁 غيّر الميعاد', 20, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('btn_cancel_booking', 1, '❌ إلغاء الحجز', 20, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('booking_rescheduled', 1, '✅ تمام، الميعاد اتغيّر ليوم {{day}} الساعة {{time}}.', 300, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('booking_cancelled', 1, 'تم إلغاء الحجز. بلّغت المكتب بخصوص الاسترداد لو حضرتك دفعت.', 300, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('btn_refund', 1, '💰 استرداد كامل', 24, true, 'proposal TASK 08', 'owner approval 2026-10-24'),
  ('booking_refund_requested', 1, 'تمام، بلّغت المكتب بطلب الاسترداد.', 300, true, 'proposal TASK 08', 'owner approval 2026-10-24')
ON CONFLICT (key, version) DO UPDATE SET active = true; -- kept by an earlier down (used in a reply)

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.booking_weekly_hours, hazem.booking_exceptions,
  hazem.booking_changes, hazem.appointments TO office_api;
GRANT USAGE, SELECT ON SEQUENCE hazem.booking_changes_id_seq TO office_api;
GRANT EXECUTE ON FUNCTION hazem.book_appointment(uuid, uuid, uuid, text, timestamptz, timestamptz, int, text, int, text),
  hazem.move_appointment(uuid, timestamptz, timestamptz, text, int, int) TO office_api;
GRANT SELECT ON hazem.booking_weekly_hours, hazem.booking_exceptions, hazem.booking_changes, hazem.appointments TO office_readonly;
REVOKE ALL ON hazem.booking_weekly_hours, hazem.booking_exceptions, hazem.booking_changes, hazem.appointments FROM office_app;
REVOKE ALL ON FUNCTION hazem.book_appointment(uuid, uuid, uuid, text, timestamptz, timestamptz, int, text, int, text),
  hazem.move_appointment(uuid, timestamptz, timestamptz, text, int, int) FROM PUBLIC;

-- migrate:down
DROP FUNCTION IF EXISTS hazem.move_appointment(uuid, timestamptz, timestamptz, text, int, int);
DROP FUNCTION IF EXISTS hazem.book_appointment(uuid, uuid, uuid, text, timestamptz, timestamptz, int, text, int, text);

-- A text already used in a reply (turn_replies' foreign key) is kept, switched off; the rest go.
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'migration 0013' OR source = 'proposal TASK 08';
DELETE FROM hazem.approved_texts t WHERE (t.updated_by = 'migration 0013' OR t.source = 'proposal TASK 08')
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
-- A button title kept above (used in a reply) can no longer hold 20/24: it becomes 300, switched off.
UPDATE hazem.approved_texts SET max_chars = 300 WHERE max_chars IN (20, 24);
ALTER TABLE hazem.approved_texts DROP CONSTRAINT approved_texts_max_chars_check;
ALTER TABLE hazem.approved_texts ADD CONSTRAINT approved_texts_max_chars_check CHECK (max_chars IN (300, 600));
DELETE FROM hazem.settings WHERE key LIKE 'booking.%' OR key LIKE 'office.%';

DELETE FROM hazem.panel_audit WHERE target_type IN ('booking', 'appointment');
ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation'));

DELETE FROM hazem.staff_tasks WHERE type IN ('booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict');
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel'));

DROP TABLE IF EXISTS hazem.appointments;
DROP TABLE IF EXISTS hazem.booking_changes;
DROP TABLE IF EXISTS hazem.booking_exceptions;
DROP TABLE IF EXISTS hazem.booking_weekly_hours;
