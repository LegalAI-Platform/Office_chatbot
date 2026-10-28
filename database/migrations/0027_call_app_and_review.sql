-- 0027_call_app_and_review: DECISIONS v1.25 (B-08, B-23, B-24). The owner's decisions of 2026-10-28:
--   call_app            a phone or video consultation asks where the lawyer calls: WhatsApp, FaceTime or Google Meet
--                       (Meet only when «بيانات المكتب» has its link). Urgent calls are WhatsApp, no question.
--   the review          the receipt image keeps the time for the client (never released by itself), but «تم تأكيد»
--                       is sent only when an office person marks the transfer «تمام» (payment_verified, the worker).
--                       An appointment still waiting for that review an hour before it starts is an urgent task.
--   office.meet_link    the lawyer's fixed Google Meet link; office.phone, the number in the office confirmation.
--   buffer              booking.buffer_minutes 10 → 0: four 15-minute calls an hour («أكتر من ميعاد في نفس الساعة»).

-- migrate:up
ALTER TABLE hazem.appointments ADD COLUMN call_app text CHECK (call_app IN ('whatsapp', 'facetime', 'meet'));

ALTER TABLE hazem.payments DROP CONSTRAINT payments_notice_kind_check;
ALTER TABLE hazem.payments ADD CONSTRAINT payments_notice_kind_check
  CHECK (notice_kind IN ('payment_partial', 'payment_not_received', 'refund_completed', 'payment_verified'));

INSERT INTO hazem.settings (key, value, description) VALUES
  ('office.meet_link', '', 'B-23 (1.25): the lawyer''s fixed Google Meet link; empty = Hazem does not offer Google Meet'),
  ('office.phone', '', 'B-08 (1.25): the office''s phone number, in the confirmation of a meeting at the office'),
  ('booking.review_alert_minutes', '60', 'B-08 (1.25): a booking whose transfer is not reviewed this many minutes before it starts is an urgent task')
ON CONFLICT (key) DO NOTHING;

UPDATE hazem.settings SET value = '0', updated_by = 'migration 0027'
 WHERE key = 'booking.buffer_minutes' AND value = '10';

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
  'service_question', 'payment_review_due'));

-- The texts, the owner's words (2026-10-28). booking_paid and reminder_details_phone get a new version.
UPDATE hazem.approved_texts SET active = false WHERE key IN ('booking_paid', 'reminder_details_phone') AND active;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('booking_paid', 2, 'وصلت صورة التحويل ✅، والميعاد محجوز لحضرتك: {{service}}، {{day}} الساعة {{time}}. أول ما المكتب يراجع التحويل، هبعتلك التأكيد.',
   300, true, 'DECISIONS v1.25 B-08', 'owner approval 2026-10-28'),
  ('booking_confirmed_call', 1, 'تم تأكيد الاستشارة {{day}} الساعة {{time}} ✅، والأستاذ هيكلم حضرتك على {{call_app}}.',
   300, true, 'DECISIONS v1.25 B-08', 'owner approval 2026-10-28'),
  ('booking_confirmed_office', 1, 'تم تأكيد المقابلة {{day}} الساعة {{time}} ✅، المكتب في {{address}}، وده اللوكيشن {{map}}. لو احتجت حاجة كلمنا على {{office_phone}}.',
   300, true, 'DECISIONS v1.25 B-08', 'owner approval 2026-10-28'),
  ('ask_call_app', 1, 'تحب المكالمة تبقى على إيه؟ 👇', 300, true, 'DECISIONS v1.25 B-23', 'owner approval 2026-10-28'),
  ('btn_app_whatsapp', 1, '🟢 واتساب', 20, true, 'DECISIONS v1.25 B-23', 'owner approval 2026-10-28'),
  ('btn_app_facetime', 1, '🍏 FaceTime (آيفون)', 20, true, 'DECISIONS v1.25 B-23', 'owner approval 2026-10-28'),
  ('btn_app_meet', 1, '🎥 Google Meet', 20, true, 'DECISIONS v1.25 B-23', 'owner approval 2026-10-28'),
  ('reminder_details_phone', 2, 'الأستاذ هيكلم حضرتك على {{call_app}} في الميعاد', 300, true, 'DECISIONS v1.25 B-18', 'owner approval 2026-10-28')
ON CONFLICT (key, version) DO NOTHING;

-- B-08 (1.25): 0016's mark_payment, and «تمام» on a booking tells the client «تم تأكيد…».
CREATE OR REPLACE FUNCTION hazem.mark_payment(p_appointment uuid, p_state text, p_received int, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.payments;
  v_over boolean;
  v_closed boolean;
  v_tell boolean;
  v_ok boolean;
BEGIN
  -- p_appointment is the payment's owner: a booking, or a service order.
  SELECT * INTO v FROM hazem.payments WHERE appointment_id = p_appointment OR service_order_id = p_appointment FOR UPDATE;
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

  -- An order the office closed, or an Urfi appointment the lawyer refused (1.15), before anyone checked its receipt:
  -- whatever came is due back (B-22).
  v_closed := p_state IN ('verified', 'partial')
              AND (EXISTS (SELECT 1 FROM hazem.service_orders WHERE id = v.service_order_id AND state = 'closed')
                   OR EXISTS (SELECT 1 FROM hazem.urf_cases WHERE appointment_id = v.appointment_id AND state = 'lawyer_rejected'));
  v_over := p_state = 'verified' AND p_received > v.amount_due;
  v_tell := p_state IN ('partial', 'not_received')
            AND (EXISTS (SELECT 1 FROM hazem.appointments WHERE id = v.appointment_id AND state = 'confirmed' AND ends_at > now())
                 OR EXISTS (SELECT 1 FROM hazem.service_orders WHERE id = v.service_order_id AND state = 'awaiting_payment'));
  -- 1.25 (B-08): «تمام» on a booking still standing tells the client «تم تأكيد…», once (not when it was already verified).
  v_ok := p_state = 'verified' AND v.state <> 'verified'
          AND EXISTS (SELECT 1 FROM hazem.appointments WHERE id = v.appointment_id AND state = 'confirmed' AND ends_at > now());
  UPDATE hazem.payments
     SET state = CASE WHEN v_over OR v_closed THEN 'refund_requested' ELSE p_state END,
         amount_received = CASE WHEN p_state = 'not_received' THEN NULL ELSE p_received END,
         refund_due = CASE WHEN v_closed THEN p_received WHEN v_over THEN p_received - v.amount_due END,
         refund_reason = CASE WHEN v_closed THEN 'office_cancelled' WHEN v_over THEN 'overpaid' END,
         reviewed_by = p_by, reviewed_at = now(),
         client_notice = CASE WHEN v_tell OR v_ok THEN 'pending' ELSE client_notice END,
         notice_kind = CASE WHEN v_tell THEN 'payment_' || p_state WHEN v_ok THEN 'payment_verified' ELSE notice_kind END
   WHERE id = v.id;
  UPDATE hazem.appointments SET calendar_state = 'update', calendar_attempts = 0, calendar_next_at = now()
   WHERE id = v.appointment_id AND state = 'confirmed' AND calendar_state = 'synced';
  -- B-16: the work starts once a person found the money.
  UPDATE hazem.service_orders SET state = 'in_progress', client_notice = 'pending', notice_kind = 'started'
   WHERE id = v.service_order_id AND state = 'awaiting_payment' AND p_state = 'verified';
  RETURN jsonb_build_object('status', 'marked', 'refund', v_over OR v_closed);
END $$;

-- migrate:down
-- 0016's mark_payment, as it was.
CREATE OR REPLACE FUNCTION hazem.mark_payment(p_appointment uuid, p_state text, p_received int, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.payments;
  v_over boolean;
  v_closed boolean;
  v_tell boolean;
BEGIN
  -- p_appointment is the payment's owner: a booking, or a service order.
  SELECT * INTO v FROM hazem.payments WHERE appointment_id = p_appointment OR service_order_id = p_appointment FOR UPDATE;
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

  -- An order the office closed, or an Urfi appointment the lawyer refused (1.15), before anyone checked its receipt:
  -- whatever came is due back (B-22).
  v_closed := p_state IN ('verified', 'partial')
              AND (EXISTS (SELECT 1 FROM hazem.service_orders WHERE id = v.service_order_id AND state = 'closed')
                   OR EXISTS (SELECT 1 FROM hazem.urf_cases WHERE appointment_id = v.appointment_id AND state = 'lawyer_rejected'));
  v_over := p_state = 'verified' AND p_received > v.amount_due;
  v_tell := p_state IN ('partial', 'not_received')
            AND (EXISTS (SELECT 1 FROM hazem.appointments WHERE id = v.appointment_id AND state = 'confirmed' AND ends_at > now())
                 OR EXISTS (SELECT 1 FROM hazem.service_orders WHERE id = v.service_order_id AND state = 'awaiting_payment'));
  UPDATE hazem.payments
     SET state = CASE WHEN v_over OR v_closed THEN 'refund_requested' ELSE p_state END,
         amount_received = CASE WHEN p_state = 'not_received' THEN NULL ELSE p_received END,
         refund_due = CASE WHEN v_closed THEN p_received WHEN v_over THEN p_received - v.amount_due END,
         refund_reason = CASE WHEN v_closed THEN 'office_cancelled' WHEN v_over THEN 'overpaid' END,
         reviewed_by = p_by, reviewed_at = now(),
         client_notice = CASE WHEN v_tell THEN 'pending' ELSE client_notice END,
         notice_kind = CASE WHEN v_tell THEN 'payment_' || p_state ELSE notice_kind END
   WHERE id = v.id;
  UPDATE hazem.appointments SET calendar_state = 'update', calendar_attempts = 0, calendar_next_at = now()
   WHERE id = v.appointment_id AND state = 'confirmed' AND calendar_state = 'synced';
  -- B-16: the work starts once a person found the money.
  UPDATE hazem.service_orders SET state = 'in_progress', client_notice = 'pending', notice_kind = 'started'
   WHERE id = v.service_order_id AND state = 'awaiting_payment' AND p_state = 'verified';
  RETURN jsonb_build_object('status', 'marked', 'refund', v_over OR v_closed);
END $$;

-- The texts: 1.25's off and gone (unless a reply used them); booking_paid and reminder_details_phone v1 back.
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-10-28' AND source LIKE 'DECISIONS v1.25%';
DELETE FROM hazem.approved_texts t WHERE t.source LIKE 'DECISIONS v1.25%'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = true WHERE key IN ('booking_paid', 'reminder_details_phone') AND version = 1
  AND NOT EXISTS (SELECT 1 FROM hazem.approved_texts a WHERE a.key = approved_texts.key AND a.active);

DELETE FROM hazem.staff_tasks WHERE type = 'payment_review_due';
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
  'service_question'));

DELETE FROM hazem.settings WHERE key IN ('office.meet_link', 'office.phone', 'booking.review_alert_minutes');

UPDATE hazem.payments SET client_notice = NULL, notice_kind = NULL WHERE notice_kind = 'payment_verified';
ALTER TABLE hazem.payments DROP CONSTRAINT payments_notice_kind_check;
ALTER TABLE hazem.payments ADD CONSTRAINT payments_notice_kind_check
  CHECK (notice_kind IN ('payment_partial', 'payment_not_received', 'refund_completed'));

ALTER TABLE hazem.appointments DROP COLUMN IF EXISTS call_app;
