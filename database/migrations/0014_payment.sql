-- 0014_payment: TASK 09 (DECISIONS v1.13, B-05 / B-08 / B-19 / B-22 / S-03 / S-04).
--   hazem.appointments      (1.13) pending_payment: the client picked a time and it is held for him until
--                           hold_until; the receipt image confirms it, otherwise it expires and the time is
--                           free again. A hold closes its time like a confirmed booking (the EXCLUDE
--                           constraint), and a client has one hold at a time (P3). A hold has no calendar
--                           event (calendar_state 'wait'); it gets one when it is confirmed.
--   hazem.payments          S-04, one per appointment. The system never confirms a payment: an office
--                           person marks verified / partial / not_received (P1) and refunded (P2).
--                           A receipt kept from an expired hold or an appointment the office cancelled
--                           pays the client's next booking of the same service (B-05 1.13, B-22).
--   hazem.payment_receipts  every receipt image (bucket, key, sha256): never read, never sent to an AI (T-09).
--   client_notice / notice_kind: what the worker still has to tell the client (never inside a request).

-- migrate:up
ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_state_check;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_state_check CHECK (state IN (
  'pending_payment', 'expired', 'confirmed', 'completed', 'no_show', 'cancelled_by_client', 'cancelled_by_office'));
ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_calendar_state_check;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_calendar_state_check
  CHECK (calendar_state IN ('wait', 'create', 'update', 'remove', 'synced', 'removed'));
ALTER TABLE hazem.appointments
  ADD COLUMN hold_until       timestamptz,
  ADD COLUMN hold_reminded_at timestamptz,
  ADD COLUMN notice_kind      text CHECK (notice_kind IN ('office_cancelled', 'hold_reminder', 'hold_expired'));
UPDATE hazem.appointments SET notice_kind = 'office_cancelled' WHERE client_notice IS NOT NULL;

ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_no_overlap;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_no_overlap
  EXCLUDE USING gist (tstzrange(starts_at, ends_at) WITH &&) WHERE (state IN ('confirmed', 'pending_payment'));
CREATE UNIQUE INDEX appointments_one_hold ON hazem.appointments (person_id) WHERE state = 'pending_payment';
CREATE INDEX appointments_hold_idx ON hazem.appointments (hold_until) WHERE state = 'pending_payment';

CREATE TABLE hazem.payments (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  appointment_id  uuid NOT NULL UNIQUE REFERENCES hazem.appointments (id),
  state           text NOT NULL DEFAULT 'requested' CHECK (state IN (
                    'requested', 'proof_received', 'duplicate_suspected', 'verified', 'partial', 'not_received',
                    'refund_requested', 'refunded')),
  amount_due      int NOT NULL CHECK (amount_due >= 0),
  amount_received int CHECK (amount_received >= 0),
  proof_at        timestamptz,                -- B-22: «وقت الدفع» = the first receipt
  reviewed_by     uuid REFERENCES hazem.panel_users (id) ON DELETE SET NULL,
  reviewed_at     timestamptz,
  refund_due      int CHECK (refund_due > 0),
  refund_reason   text CHECK (refund_reason IN ('client_within_24h', 'office_cancelled', 'overpaid')),
  refunded_by     uuid REFERENCES hazem.panel_users (id) ON DELETE SET NULL,
  refunded_at     timestamptz,
  refund_method   text CHECK (char_length(refund_method) <= 100),
  refund_note     text CHECK (char_length(refund_note) <= 200),
  client_notice   text CHECK (client_notice IN ('pending', 'sent', 'failed')),
  notice_kind     text CHECK (notice_kind IN ('payment_partial', 'payment_not_received', 'refund_completed')),
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX payments_state_idx ON hazem.payments (state);
CREATE INDEX payments_notice_idx ON hazem.payments (updated_at) WHERE client_notice = 'pending';
CREATE TRIGGER trg_payments_updated_at BEFORE UPDATE ON hazem.payments
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- The appointments booked before 0014 (confirmed at once, TASK 08): their payment is still requested.
INSERT INTO hazem.payments (appointment_id, amount_due)
SELECT id, amount FROM hazem.appointments ON CONFLICT (appointment_id) DO NOTHING;

CREATE TABLE hazem.payment_receipts (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  payment_id   uuid NOT NULL REFERENCES hazem.payments (id),
  wamid        text NOT NULL UNIQUE,
  bucket       text NOT NULL,
  key          text NOT NULL,
  sha256       text NOT NULL,
  mime         text,
  duplicate_of uuid REFERENCES hazem.payments (id),
  received_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX payment_receipts_payment_idx ON hazem.payment_receipts (payment_id, received_at);
CREATE INDEX payment_receipts_sha_idx ON hazem.payment_receipts (sha256);

-- 1.13: the hold's length (the panel's booking settings).
INSERT INTO hazem.settings (key, value, description) VALUES
  ('booking.hold_minutes', '120', 'B-08 (1.13): how long a picked time waits for the receipt image'),
  ('booking.hold_short_minutes', '60', 'B-08 (1.13): the hold when the appointment is soon (booking.hold_short_within_hours)'),
  ('booking.hold_short_within_hours', '6', 'B-08 (1.13): an appointment starting within this many hours gets the short hold'),
  ('booking.hold_gap_minutes', '15', 'B-08 (1.13): a hold always ends at least this many minutes before the appointment')
ON CONFLICT (key) DO NOTHING;

-- B-08.3 (1.13): the time is held for the client until p_hold_until, or confirmed at once when a receipt
-- he already sent (an expired hold, or a booking the office cancelled) pays for it. The API checked
-- Google Calendar for this time just before.
DROP FUNCTION hazem.book_appointment(uuid, uuid, uuid, text, timestamptz, timestamptz, int, text, int, text);
CREATE FUNCTION hazem.book_appointment(
  p_id uuid, p_person uuid, p_intake uuid, p_service text, p_start timestamptz, p_end timestamptz,
  p_amount int, p_turn text, p_buffer_minutes int, p_matter text, p_hold_until timestamptz)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_id uuid;
  v_state text;
  v_hold hazem.appointments;
  v_credit uuid;
  v_until timestamptz := p_hold_until;
  v_buffer interval := make_interval(mins => GREATEST(p_buffer_minutes, 0));
BEGIN
  SELECT id, state INTO v_id, v_state FROM hazem.appointments WHERE booked_turn_id = p_turn;
  IF FOUND THEN
    RETURN jsonb_build_object('status', 'exists', 'id', v_id, 'state', v_state); -- the same turn again (a retry)
  END IF;

  PERFORM pg_advisory_xact_lock(hashtext('hazem.appointments'));

  SELECT id INTO v_id FROM hazem.appointments
   WHERE person_id = p_person AND service = p_service AND state = 'confirmed' AND ends_at > now()
   LIMIT 1;
  IF FOUND THEN
    RETURN jsonb_build_object('status', 'already_booked', 'id', v_id); -- B-08: one open booking per service
  END IF;

  SELECT * INTO v_hold FROM hazem.appointments WHERE person_id = p_person AND state = 'pending_payment';
  IF FOUND THEN
    IF v_hold.service <> p_service AND v_hold.hold_until > now() THEN
      RETURN jsonb_build_object('status', 'has_hold', 'id', v_hold.id); -- P3: one hold at a time
    END IF;
    -- B-15 (1.13): another time for the same service replaces the hold, never for longer than it had.
    IF v_hold.hold_until > now() THEN
      v_until := LEAST(p_hold_until, v_hold.hold_until);
    END IF;
    UPDATE hazem.appointments SET state = 'expired', calendar_state = 'removed' WHERE id = v_hold.id;
  END IF;

  IF EXISTS (SELECT 1 FROM hazem.appointments
              WHERE state IN ('confirmed', 'pending_payment')
                AND tstzrange(starts_at - v_buffer, ends_at + v_buffer) && tstzrange(p_start, p_end)) THEN
    RETURN jsonb_build_object('status', 'taken');
  END IF;

  SELECT p.id INTO v_credit FROM hazem.payments p JOIN hazem.appointments a ON a.id = p.appointment_id
   WHERE a.person_id = p_person AND a.service = p_service AND a.state IN ('expired', 'cancelled_by_office')
     AND p.proof_at IS NOT NULL
     AND p.state IN ('proof_received', 'duplicate_suspected', 'verified', 'partial', 'not_received')
   ORDER BY p.proof_at DESC LIMIT 1;

  IF v_credit IS NOT NULL THEN
    INSERT INTO hazem.appointments (id, person_id, intake_id, service, starts_at, ends_at, amount, matter_type, booked_turn_id,
                                    state, calendar_state)
    VALUES (p_id, p_person, p_intake, p_service, p_start, p_end, p_amount, p_matter, p_turn, 'confirmed', 'create');
    UPDATE hazem.payments SET appointment_id = p_id WHERE id = v_credit;
    RETURN jsonb_build_object('status', 'booked_paid', 'id', p_id);
  END IF;

  INSERT INTO hazem.appointments (id, person_id, intake_id, service, starts_at, ends_at, amount, matter_type, booked_turn_id,
                                  state, calendar_state, hold_until)
  VALUES (p_id, p_person, p_intake, p_service, p_start, p_end, p_amount, p_matter, p_turn, 'pending_payment', 'wait', v_until);
  INSERT INTO hazem.payments (appointment_id, amount_due) VALUES (p_id, p_amount);
  RETURN jsonb_build_object('status', 'held', 'id', p_id);
END $$;

-- B-15: unchanged, except that a hold closes its time too (and a hold is never moved: it is replaced).
DROP FUNCTION hazem.move_appointment(uuid, timestamptz, timestamptz, text, int, int);
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
              WHERE id <> p_id AND state IN ('confirmed', 'pending_payment')
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

-- B-05 (1.13): a receipt image from the client, for (1) his hold → confirmed, (2) else his next confirmed
-- booking still waiting for a (new) receipt, (3) else a hold whose time ran out: confirmed again when the
-- time is still free (p_late_free: the API checked the calendar and the hours), otherwise kept as a credit
-- for the time he picks next. The same image on another booking: duplicate_suspected, an alert only.
CREATE FUNCTION hazem.attach_receipt(
  p_person uuid, p_wamid text, p_bucket text, p_key text, p_sha256 text, p_mime text, p_late_free boolean, p_buffer_minutes int)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_a hazem.appointments;
  v_p hazem.payments;
  v_status text;
  v_dup uuid;
  v_buffer interval := make_interval(mins => GREATEST(p_buffer_minutes, 0));
BEGIN
  SELECT a.* INTO v_a FROM hazem.payment_receipts r JOIN hazem.payments p ON p.id = r.payment_id
    JOIN hazem.appointments a ON a.id = p.appointment_id WHERE r.wamid = p_wamid;
  IF FOUND THEN
    RETURN jsonb_build_object('status', 'exists', 'id', v_a.id, 'state', v_a.state); -- the same message again
  END IF;

  PERFORM pg_advisory_xact_lock(hashtext('hazem.appointments'));

  SELECT * INTO v_a FROM hazem.appointments WHERE person_id = p_person AND state = 'pending_payment' FOR UPDATE;
  IF FOUND THEN
    -- Still held (even a moment after its time, before the worker freed it): nobody else has it.
    UPDATE hazem.appointments
       SET state = 'confirmed', calendar_state = 'create', calendar_attempts = 0, calendar_next_at = now(),
           client_notice = CASE WHEN notice_kind = 'hold_reminder' AND client_notice = 'pending' THEN NULL ELSE client_notice END
     WHERE id = v_a.id;
    v_status := 'confirmed';
  ELSE
    SELECT a.* INTO v_a FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
     WHERE a.person_id = p_person AND a.state = 'confirmed' AND a.ends_at > now()
       AND p.state IN ('partial', 'not_received', 'requested')
     ORDER BY (p.state = 'requested'), a.starts_at LIMIT 1;
    IF FOUND THEN
      v_status := 'received';
    ELSE
      SELECT a.* INTO v_a FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
       WHERE a.person_id = p_person AND a.state = 'expired' AND a.starts_at > now() AND p.state = 'requested'
       ORDER BY a.updated_at DESC LIMIT 1;
      IF NOT FOUND THEN
        RETURN jsonb_build_object('status', 'none');
      END IF;

      IF p_late_free AND NOT EXISTS (SELECT 1 FROM hazem.appointments
                                      WHERE id <> v_a.id AND state IN ('confirmed', 'pending_payment')
                                        AND tstzrange(starts_at - v_buffer, ends_at + v_buffer) && tstzrange(v_a.starts_at, v_a.ends_at)) THEN
        UPDATE hazem.appointments
           SET state = 'confirmed', calendar_state = 'create', calendar_attempts = 0, calendar_next_at = now(), client_notice = NULL
         WHERE id = v_a.id;
        v_status := 'confirmed_late';
      ELSE
        v_status := 'late_taken';
      END IF;
    END IF;
  END IF;

  SELECT * INTO v_p FROM hazem.payments WHERE appointment_id = v_a.id FOR UPDATE;
  IF NOT FOUND THEN
    INSERT INTO hazem.payments (appointment_id, amount_due) VALUES (v_a.id, v_a.amount) RETURNING * INTO v_p;
  END IF;

  SELECT r.payment_id INTO v_dup FROM hazem.payment_receipts r WHERE r.sha256 = p_sha256 AND r.payment_id <> v_p.id LIMIT 1;
  INSERT INTO hazem.payment_receipts (payment_id, wamid, bucket, key, sha256, mime, duplicate_of)
  VALUES (v_p.id, p_wamid, p_bucket, p_key, p_sha256, p_mime, v_dup);
  UPDATE hazem.payments
     SET state = CASE WHEN v_dup IS NOT NULL THEN 'duplicate_suspected'
                      WHEN state IN ('requested', 'partial', 'not_received') THEN 'proof_received'
                      ELSE state END,
         proof_at = COALESCE(proof_at, now())
   WHERE id = v_p.id;
  -- P6: the calendar event shows the review; a new receipt means «waiting for the review» again.
  UPDATE hazem.appointments SET calendar_state = 'update', calendar_attempts = 0, calendar_next_at = now()
   WHERE id = v_a.id AND calendar_state = 'synced';
  RETURN jsonb_build_object('status', v_status, 'id', v_a.id, 'duplicate', v_dup IS NOT NULL);
END $$;

-- B-08.6 (1.13), run by the worker: one reminder before a hold's time runs out (when the hold is long
-- enough for it to mean something), then the hold expires and its time is free again.
CREATE FUNCTION hazem.booking_due(p_remind_before_minutes int, p_min_hold_minutes int)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_reminded int;
  v_expired int;
BEGIN
  PERFORM pg_advisory_xact_lock(hashtext('hazem.appointments'));
  UPDATE hazem.appointments
     SET hold_reminded_at = now(), client_notice = 'pending', notice_kind = 'hold_reminder'
   WHERE state = 'pending_payment' AND hold_reminded_at IS NULL AND hold_until > now()
     AND hold_until - make_interval(mins => p_remind_before_minutes) <= now()
     AND hold_until - created_at >= make_interval(mins => p_min_hold_minutes);
  GET DIAGNOSTICS v_reminded = ROW_COUNT;
  UPDATE hazem.appointments
     SET state = 'expired', calendar_state = 'removed', client_notice = 'pending', notice_kind = 'hold_expired'
   WHERE state = 'pending_payment' AND hold_until <= now();
  GET DIAGNOSTICS v_expired = ROW_COUNT;
  RETURN jsonb_build_object('reminded', v_reminded, 'expired', v_expired);
END $$;

-- B-05 / S-04: an office person marks what he found (P1). The three marks may correct each other until a
-- refund starts. Verified with more than due → refund_requested for the difference (P4). Partial and
-- not received are told to the client by the worker.
CREATE FUNCTION hazem.mark_payment(p_appointment uuid, p_state text, p_received int, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.payments;
  v_over boolean;
BEGIN
  SELECT * INTO v FROM hazem.payments WHERE appointment_id = p_appointment FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF p_state NOT IN ('verified', 'partial', 'not_received') THEN
    RETURN jsonb_build_object('status', 'invalid');
  END IF;
  IF NOT (v.state IN ('proof_received', 'duplicate_suspected', 'verified', 'partial', 'not_received')
          OR (v.state = 'requested' AND p_state = 'not_received')) THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  IF (p_state = 'verified' AND (p_received IS NULL OR p_received < v.amount_due))
     OR (p_state = 'partial' AND (p_received IS NULL OR p_received <= 0 OR p_received >= v.amount_due)) THEN
    RETURN jsonb_build_object('status', 'invalid_amount');
  END IF;

  v_over := p_state = 'verified' AND p_received > v.amount_due;
  UPDATE hazem.payments
     SET state = CASE WHEN v_over THEN 'refund_requested' ELSE p_state END,
         amount_received = CASE WHEN p_state = 'not_received' THEN NULL ELSE p_received END,
         refund_due = CASE WHEN v_over THEN p_received - v.amount_due END,
         refund_reason = CASE WHEN v_over THEN 'overpaid' END,
         reviewed_by = p_by, reviewed_at = now(),
         client_notice = CASE WHEN p_state IN ('partial', 'not_received') THEN 'pending' ELSE client_notice END,
         notice_kind = CASE WHEN p_state IN ('partial', 'not_received') THEN 'payment_' || p_state ELSE notice_kind END
   WHERE id = v.id;
  UPDATE hazem.appointments SET calendar_state = 'update', calendar_attempts = 0, calendar_next_at = now()
   WHERE id = p_appointment AND state = 'confirmed' AND calendar_state = 'synced';
  RETURN jsonb_build_object('status', 'marked', 'refund', v_over);
END $$;

-- B-22: a refund is due (the client cancelled within 24 hours of his receipt, or chose the refund after
-- the office cancelled). Only money that came: a receipt at least. Returns the amount, or null.
CREATE FUNCTION hazem.request_refund(p_appointment uuid, p_reason text)
RETURNS int LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_due int;
BEGIN
  UPDATE hazem.payments
     SET state = 'refund_requested', refund_due = COALESCE(amount_received, amount_due), refund_reason = p_reason
   WHERE appointment_id = p_appointment AND state IN ('proof_received', 'duplicate_suspected', 'verified', 'partial')
         AND COALESCE(amount_received, amount_due) > 0
  RETURNING refund_due INTO v_due;
  RETURN v_due;
END $$;

-- B-22 (P2): a person of the office transferred the money back. Never the system.
CREATE FUNCTION hazem.mark_refunded(p_appointment uuid, p_by uuid, p_method text, p_note text)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.payments;
BEGIN
  SELECT * INTO v FROM hazem.payments WHERE appointment_id = p_appointment FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v.state <> 'refund_requested' THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  UPDATE hazem.payments
     SET state = 'refunded', refunded_by = p_by, refunded_at = now(), refund_method = p_method, refund_note = p_note,
         client_notice = 'pending', notice_kind = 'refund_completed'
   WHERE id = v.id;
  RETURN jsonb_build_object('status', 'refunded');
END $$;

ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation', 'booking', 'appointment', 'payment'));

-- Proposed in TASK 09 and approved by the owner as proposed (2026-10-24, in chat): active. The booking
-- runs only when every one of them is active (HazemResponder.BookingTextKeys).
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('booking_hold', 1, '⏳ ميعاد حضرتك متسجّل مبدئيًا: {{service}}، {{day}} الساعة {{time}}.

عشان الميعاد يتأكد:
1️⃣ حوّل {{amount}} جنيه على InstaPay: {{instapay}}، أو محفظة كاش: {{wallet}}.
2️⃣ ابعت صورة (سكرين شوت) التحويل هنا في الشات.

⏰ لازم الصورة توصل قبل الساعة {{deadline}}، وإلا الميعاد هيتلغي ويبقى متاح لحد تاني.', 600, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('booking_hold_reminder', 1, '⏰ فاضل نص ساعة: ميعاد حضرتك {{day}} الساعة {{time}} لسه مستني صورة التحويل.
لو ما وصلتش قبل الساعة {{deadline}} الميعاد هيتلغي.
التحويل {{amount}} جنيه على InstaPay: {{instapay}}، أو محفظة كاش: {{wallet}}.', 600, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('booking_paid', 1, '✅ وصلت صورة التحويل، وميعاد حضرتك اتأكد: {{service}}، {{day}} الساعة {{time}}.
الأستاذ هيراجع التحويل قبل الميعاد، ولو فيه أي حاجة هنكلّم حضرتك هنا.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('booking_hold_expired', 1, '❌ ميعاد {{day}} الساعة {{time}} اتلغى لأن صورة التحويل ما وصلتش في الوقت.
لو لسه عايز تحجز، ابعت «احجز» ونختار ميعاد جديد.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('booking_hold_late', 1, 'وصلت صورة التحويل ✅، بس ميعاد {{day}} الساعة {{time}} اتحجز لحد تاني.
اختار ميعاد من القايمة، ويتأكد على طول من غير ما تدفع تاني 👇', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('booking_hold_one', 1, 'عند حضرتك ميعاد {{day}} الساعة {{time}} مستني صورة التحويل لحد الساعة {{deadline}}.
ابعت الصورة الأول، وبعدين نحجز التاني.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('booking_hold_cancelled', 1, 'تمام، اتلغى الميعاد اللي كان متسجّل مبدئيًا.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('booking_office_unpaid', 1, 'نعتذر لحضرتك، الأستاذ عنده ظرف في ميعاد {{day}} الساعة {{time}}. اختار ميعاد تاني من القايمة 👇', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('receipt_send_image', 1, 'من فضلك ابعت صورة (سكرين شوت) للتحويل، عشان الميعاد يتأكد.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('proof_received_again', 1, 'استلمنا صورة التحويل ✅، وهنراجعها ونبلّغ حضرتك لو فيه أي حاجة.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('payment_partial', 1, '⚠️ وصلنا {{received}} جنيه من {{amount}} جنيه لميعاد {{day}} الساعة {{time}}.
محتاجين باقي المبلغ {{rest}} جنيه، وبعد ما تحوّل ابعت صورة التحويل هنا.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('payment_not_received', 1, '⚠️ تحويل ميعاد {{day}} الساعة {{time}} ما وصلش لحد دلوقتي.
ممكن حضرتك تتأكد من التحويل وتبعت صورته تاني هنا؟', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('refund_full', 1, 'تم إلغاء الحجز. وبما إن الإلغاء خلال 24 ساعة من الدفع، المبلغ هيرجعلك كامل، والمكتب هيحوّله ويبلّغ حضرتك.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('refund_none', 1, 'تم إلغاء الحجز. الإلغاء بعد 24 ساعة من الدفع، فمفيش استرداد حسب سياسة المكتب.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24'),
  ('refund_completed', 1, '✅ تم رد {{amount}} جنيه لحضرتك على {{method}}.', 300, true, 'proposal TASK 09', 'owner approval 2026-10-24')
ON CONFLICT (key, version) DO UPDATE SET active = true; -- kept by an earlier down (used in a reply)

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.payments, hazem.payment_receipts TO office_api;
GRANT EXECUTE ON FUNCTION hazem.book_appointment(uuid, uuid, uuid, text, timestamptz, timestamptz, int, text, int, text, timestamptz),
  hazem.move_appointment(uuid, timestamptz, timestamptz, text, int, int),
  hazem.attach_receipt(uuid, text, text, text, text, text, boolean, int),
  hazem.booking_due(int, int),
  hazem.mark_payment(uuid, text, int, uuid),
  hazem.request_refund(uuid, text),
  hazem.mark_refunded(uuid, uuid, text, text) TO office_api;
GRANT SELECT ON hazem.payments, hazem.payment_receipts TO office_readonly;
REVOKE ALL ON hazem.payments, hazem.payment_receipts FROM office_app;
REVOKE ALL ON FUNCTION hazem.book_appointment(uuid, uuid, uuid, text, timestamptz, timestamptz, int, text, int, text, timestamptz),
  hazem.move_appointment(uuid, timestamptz, timestamptz, text, int, int),
  hazem.attach_receipt(uuid, text, text, text, text, text, boolean, int),
  hazem.booking_due(int, int),
  hazem.mark_payment(uuid, text, int, uuid),
  hazem.request_refund(uuid, text),
  hazem.mark_refunded(uuid, uuid, text, text) FROM PUBLIC;

-- migrate:down
DROP FUNCTION IF EXISTS hazem.mark_refunded(uuid, uuid, text, text);
DROP FUNCTION IF EXISTS hazem.request_refund(uuid, text);
DROP FUNCTION IF EXISTS hazem.mark_payment(uuid, text, int, uuid);
DROP FUNCTION IF EXISTS hazem.booking_due(int, int);
DROP FUNCTION IF EXISTS hazem.attach_receipt(uuid, text, text, text, text, text, boolean, int);
DROP FUNCTION IF EXISTS hazem.book_appointment(uuid, uuid, uuid, text, timestamptz, timestamptz, int, text, int, text, timestamptz);
DROP FUNCTION IF EXISTS hazem.move_appointment(uuid, timestamptz, timestamptz, text, int, int);

-- A text already used in a reply (turn_replies' foreign key) is kept, switched off; the rest go.
UPDATE hazem.approved_texts SET active = false WHERE source = 'proposal TASK 09';
DELETE FROM hazem.approved_texts t WHERE t.source = 'proposal TASK 09'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
DELETE FROM hazem.settings WHERE key IN ('booking.hold_minutes', 'booking.hold_short_minutes', 'booking.hold_short_within_hours',
  'booking.hold_gap_minutes');

DELETE FROM hazem.panel_audit WHERE target_type = 'payment';
ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation', 'booking', 'appointment'));

DROP TABLE IF EXISTS hazem.payment_receipts;
DROP TABLE IF EXISTS hazem.payments;

-- 0013 had no hold: a time still held is given up as the client's own cancellation (nothing was paid),
-- and an expired hold the same.
UPDATE hazem.appointments SET state = 'cancelled_by_client' WHERE state IN ('pending_payment', 'expired');
UPDATE hazem.appointments SET calendar_state = 'removed' WHERE calendar_state = 'wait';
UPDATE hazem.appointments SET client_notice = NULL WHERE notice_kind IN ('hold_reminder', 'hold_expired');
DROP INDEX IF EXISTS hazem.appointments_hold_idx;
DROP INDEX IF EXISTS hazem.appointments_one_hold;
ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_no_overlap;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_no_overlap
  EXCLUDE USING gist (tstzrange(starts_at, ends_at) WITH &&) WHERE (state = 'confirmed');
ALTER TABLE hazem.appointments DROP COLUMN notice_kind, DROP COLUMN hold_reminded_at, DROP COLUMN hold_until;
ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_calendar_state_check;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_calendar_state_check
  CHECK (calendar_state IN ('create', 'update', 'remove', 'synced', 'removed'));
ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_state_check;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_state_check
  CHECK (state IN ('confirmed', 'completed', 'no_show', 'cancelled_by_client', 'cancelled_by_office'));

-- 0013's functions, as they were.
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

GRANT EXECUTE ON FUNCTION hazem.book_appointment(uuid, uuid, uuid, text, timestamptz, timestamptz, int, text, int, text),
  hazem.move_appointment(uuid, timestamptz, timestamptz, text, int, int) TO office_api;
REVOKE ALL ON FUNCTION hazem.book_appointment(uuid, uuid, uuid, text, timestamptz, timestamptz, int, text, int, text),
  hazem.move_appointment(uuid, timestamptz, timestamptz, text, int, int) FROM PUBLIC;
