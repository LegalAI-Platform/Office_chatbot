-- 0035_urgent_callback: DECISIONS v1.33 (B-09). The owner (2026-10-31, «موافق نفذ»): with the booking on, the urgent
-- consultation is a call from the lawyer after the payment, not a time the client picks.
--   hazem.appointments  an urgent call is held at the first free moment (off the slot grid), and the receipt sets it again
--                       at the first free moment from then (hazem.retime_urgent). urgent_alerted_at / urgent_alert_receipt:
--                       the template urgent_alert went to office.lawyer_whatsapp (or could not) for that receipt; a new
--                       receipt after «فيه مشكلة» alerts again. urgent_acked_at: «التحويل تمام، هكلمه»; urgent_escalated_at:
--                       the alert not decided in time (the manager's template); urgent_waiting_at: urgent_waiting told, once.
--   hazem.payment_receipts  urgent_decision: the lawyer's (or the manager's) press on an urgent call's receipt, ok or bad.
--                       The first decision on the call's latest receipt wins (a conditional UPDATE in the API).
--   hazem.settings      office.lawyer_whatsapp, office.manager_whatsapp (digits, from «بيانات المكتب»),
--                       urgent.call_within_minutes and urgent.escalate_after_minutes.
--   hazem.staff_tasks   urgent_call_failed (no lawyer's number, a send refused) and urgent_call_unanswered (the escalation):
--                       both an urgent bell in the panel.
--   hazem.mark_payment  «تمام» on an urgent call sends no «تم تأكيد» (the lawyer's call is its confirmation).
--   hazem.file_target   a late receipt for an urgent call whose hold ran out is still its receipt (its time is set again), and
--                       a new receipt after «فيه مشكلة» is the call's even once its quarter of an hour has passed.
-- The texts, word for word in DECISIONS v1.33 B-09 (the slots as [المبلغ] [العنوان] [الرقم] [بداية الشغل] [الساعة] [الاسم]).

-- migrate:up
INSERT INTO hazem.settings (key, value, description) VALUES
  ('office.lawyer_whatsapp', '', 'B-09 (1.33): the lawyer''s WhatsApp number (digits): the template urgent_alert goes to it'),
  ('office.manager_whatsapp', '', 'B-09 (1.33): the manager''s WhatsApp number (digits): urgent_alert when the lawyer does not press «هكلمه»'),
  ('urgent.call_within_minutes', '10', 'B-09 (1.33): the lawyer is alerted this many minutes before the urgent call (urgent_now within it)'),
  ('urgent.escalate_after_minutes', '10', 'B-09 (1.33): «هكلمه» not pressed this many minutes after the alert → the manager and urgent_waiting')
ON CONFLICT (key) DO NOTHING;

ALTER TABLE hazem.appointments
  ADD COLUMN urgent_alerted_at    timestamptz,
  ADD COLUMN urgent_alert_receipt uuid REFERENCES hazem.payment_receipts (id),
  ADD COLUMN urgent_acked_at      timestamptz,
  ADD COLUMN urgent_escalated_at  timestamptz,
  ADD COLUMN urgent_waiting_at    timestamptz;
CREATE INDEX appointments_urgent_idx ON hazem.appointments (starts_at)
  WHERE service = 'consultation_urgent' AND state = 'confirmed';
ALTER TABLE hazem.payment_receipts
  ADD COLUMN urgent_decision   text CHECK (urgent_decision IN ('ok', 'bad')),
  ADD COLUMN urgent_decided_at timestamptz;

-- B-09 (1.33): an urgent call (held, its hold run out, or confirmed) set at another time, off the slot grid, clear of every
-- other held or confirmed appointment with the buffer (the API checked the calendar and the hours just before). Never the
-- client's free change (B-15). Its alert starts again. False: the time is taken (or it is not an urgent call).
CREATE FUNCTION hazem.retime_urgent(p_id uuid, p_start timestamptz, p_end timestamptz, p_buffer_minutes int)
RETURNS boolean LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.appointments;
  v_buffer interval := make_interval(mins => GREATEST(p_buffer_minutes, 0));
BEGIN
  PERFORM pg_advisory_xact_lock(hashtext('hazem.appointments'));
  SELECT * INTO v FROM hazem.appointments WHERE id = p_id FOR UPDATE;
  IF NOT FOUND OR v.service <> 'consultation_urgent' OR v.state NOT IN ('pending_payment', 'expired', 'confirmed') OR p_end <= p_start THEN
    RETURN false;
  END IF;
  IF EXISTS (SELECT 1 FROM hazem.appointments
              WHERE id <> p_id AND state IN ('confirmed', 'pending_payment')
                AND tstzrange(starts_at - v_buffer, ends_at + v_buffer) && tstzrange(p_start, p_end)) THEN
    RETURN false;
  END IF;

  UPDATE hazem.appointments
     SET starts_at = p_start, ends_at = p_end,
         urgent_alerted_at = NULL, urgent_alert_receipt = NULL, urgent_acked_at = NULL, urgent_escalated_at = NULL,
         calendar_state = CASE WHEN calendar_event_id IS NULL THEN calendar_state ELSE 'update' END,
         calendar_attempts = 0, calendar_next_at = now(), conflict_noted_at = NULL
   WHERE id = p_id;
  RETURN true;
END $$;

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
  'rating_low',
  'urgent_call_failed', 'urgent_call_unanswered'));

-- v1.33 (B-09): 0016's file_target, and a late receipt for an urgent call whose hold ran out (its time is set again).
CREATE OR REPLACE FUNCTION hazem.file_target(p_person uuid, p_image boolean)
RETURNS TABLE (kind text, id uuid) LANGUAGE plpgsql STABLE SET search_path = hazem, pg_temp AS $$
DECLARE
  v_docs uuid;
  v_urf uuid;
  v_kind text;
  v_id uuid;
BEGIN
  SELECT o.id INTO v_docs FROM hazem.service_orders o
   WHERE o.person_id = p_person AND o.state IN ('collecting', 'awaiting_price') ORDER BY o.created_at DESC LIMIT 1;
  SELECT c.id INTO v_urf FROM hazem.urf_cases c
   WHERE c.person_id = p_person AND c.state IN ('booked', 'approved') -- a case with a live time only: never a stale one
     AND EXISTS (SELECT 1 FROM hazem.appointments a WHERE a.id = c.appointment_id AND a.state IN ('pending_payment', 'confirmed') AND a.ends_at > now())
   ORDER BY c.created_at DESC LIMIT 1;

  SELECT 'hold', a.id INTO v_kind, v_id FROM hazem.appointments a WHERE a.person_id = p_person AND a.state = 'pending_payment' LIMIT 1;
  IF v_id IS NULL THEN
    SELECT 'order_payment', o.id INTO v_kind, v_id FROM hazem.service_orders o JOIN hazem.payments p ON p.service_order_id = o.id
     WHERE o.person_id = p_person AND o.state = 'awaiting_payment' AND p.state IN ('requested', 'partial', 'not_received')
     ORDER BY o.price_until LIMIT 1;
  END IF;
  IF v_id IS NULL THEN
    SELECT 'booking_payment', a.id INTO v_kind, v_id FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
     WHERE a.person_id = p_person AND a.state = 'confirmed' AND p.state IN ('partial', 'not_received', 'requested')
       -- v1.33 (B-09): a new receipt after «فيه مشكلة» is the urgent call's, even past its quarter of an hour (a day at most)
       AND (a.ends_at > now() OR a.service = 'consultation_urgent' AND a.ends_at > now() - interval '1 day')
     ORDER BY (p.state = 'requested'), a.starts_at LIMIT 1;
  END IF;
  IF v_id IS NULL THEN
    SELECT 'late_hold', a.id INTO v_kind, v_id FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
     WHERE a.person_id = p_person AND a.state = 'expired' AND p.state = 'requested'
       AND (a.starts_at > now()
            -- v1.33 (B-09): an urgent call is set again from the receipt: its old time does not matter (a day at most)
            OR a.service = 'consultation_urgent' AND a.hold_until > now() - interval '1 day')
     ORDER BY a.updated_at DESC LIMIT 1;
  END IF;

  IF p_image THEN
    IF v_kind IN ('hold', 'order_payment', 'booking_payment') THEN
      RETURN QUERY SELECT v_kind, v_id;
    ELSIF v_urf IS NOT NULL THEN
      RETURN QUERY SELECT 'urf_documents'::text, v_urf;
    ELSIF v_docs IS NOT NULL THEN
      RETURN QUERY SELECT 'order_documents'::text, v_docs;
    ELSIF v_id IS NOT NULL THEN
      RETURN QUERY SELECT v_kind, v_id;
    END IF;
  ELSIF v_urf IS NOT NULL THEN
    RETURN QUERY SELECT 'urf_documents'::text, v_urf;
  ELSIF v_docs IS NOT NULL THEN
    RETURN QUERY SELECT 'order_documents'::text, v_docs;
  ELSIF v_id IS NOT NULL THEN
    RETURN QUERY SELECT 'ask_image'::text, v_id;
  END IF;
END $$;

-- v1.33 (B-09): 0027's mark_payment; «تمام» on an urgent call tells the client nothing.
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
  -- v1.33 (B-09): never for an urgent call (the lawyer's call is its confirmation).
  v_ok := p_state = 'verified' AND v.state <> 'verified'
          AND EXISTS (SELECT 1 FROM hazem.appointments WHERE id = v.appointment_id AND state = 'confirmed' AND ends_at > now()
                        AND service <> 'consultation_urgent');
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

-- The texts, the owner's words (2026-10-31), quoted word for word in DECISIONS v1.33 B-09. urgent_alert is the M-03
-- template's body as submitted to Meta (n8n/templates/whatsapp-templates.json, {{1}} {{2}} {{3}} there), btn_urgent_see its
-- quick reply; btn_urgent_ok and btn_urgent_bad the two buttons under the receipt. Without every one of them the urgent path
-- stays as in 1.32.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urgent_pay', 1, 'عشان المستشار يكلم حضرتك في أسرع وقت، حوّل {{amount}} على InstaPay: {{instapay}}، أو محفظة كاش: {{wallet}}، وابعت صورة التحويل هنا.', 300, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31'),
  ('urgent_night', 1, 'المستشار بيرد على الاستشارات العاجلة من الساعة {{opens}}. لو حوّلت دلوقتي، هيكلم حضرتك أول حاجة الساعة {{opens}} الصبح.', 300, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31'),
  ('urgent_now', 1, 'وصلت صورة التحويل ✅. المستشار هيكلم حضرتك على الرقم ده خلال 10 دقايق.', 300, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31'),
  ('urgent_after', 1, 'وصلت صورة التحويل ✅. المستشار في مكالمة دلوقتي، وهيكلم حضرتك على الرقم ده حوالي الساعة {{time}}.', 300, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31'),
  ('urgent_seen', 1, 'المستشار شاف طلب حضرتك، وهيكلمك دلوقتي.', 300, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31'),
  ('urgent_waiting', 1, 'بنوصّل للمستشار، ثواني وهيكلم حضرتك.', 300, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31'),
  ('urgent_alert', 1, '🚨 استشارة عاجلة مدفوعة: {{name}}، {{phone}}. المكالمة الساعة {{time}}. اتأكد من التحويل في الصورة.', 300, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31'),
  ('btn_urgent_see', 1, 'شوف التحويل', 20, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31'),
  ('btn_urgent_ok', 1, 'التحويل تمام، هكلمه', 20, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31'),
  ('btn_urgent_bad', 1, 'فيه مشكلة', 20, true, 'DECISIONS v1.33 B-09', 'owner approval 2026-10-31')
ON CONFLICT (key, version) DO NOTHING;

GRANT EXECUTE ON FUNCTION hazem.retime_urgent(uuid, timestamptz, timestamptz, int) TO office_api;
REVOKE ALL ON FUNCTION hazem.retime_urgent(uuid, timestamptz, timestamptz, int) FROM PUBLIC;

-- migrate:down
UPDATE hazem.approved_texts SET active = false WHERE source = 'DECISIONS v1.33 B-09';
DELETE FROM hazem.approved_texts t
 WHERE t.source = 'DECISIONS v1.33 B-09'
   AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);

-- 0016's file_target and 0027's mark_payment, as they were.
CREATE OR REPLACE FUNCTION hazem.file_target(p_person uuid, p_image boolean)
RETURNS TABLE (kind text, id uuid) LANGUAGE plpgsql STABLE SET search_path = hazem, pg_temp AS $$
DECLARE
  v_docs uuid;
  v_urf uuid;
  v_kind text;
  v_id uuid;
BEGIN
  SELECT o.id INTO v_docs FROM hazem.service_orders o
   WHERE o.person_id = p_person AND o.state IN ('collecting', 'awaiting_price') ORDER BY o.created_at DESC LIMIT 1;
  SELECT c.id INTO v_urf FROM hazem.urf_cases c
   WHERE c.person_id = p_person AND c.state IN ('booked', 'approved') -- a case with a live time only: never a stale one
     AND EXISTS (SELECT 1 FROM hazem.appointments a WHERE a.id = c.appointment_id AND a.state IN ('pending_payment', 'confirmed') AND a.ends_at > now())
   ORDER BY c.created_at DESC LIMIT 1;

  SELECT 'hold', a.id INTO v_kind, v_id FROM hazem.appointments a WHERE a.person_id = p_person AND a.state = 'pending_payment' LIMIT 1;
  IF v_id IS NULL THEN
    SELECT 'order_payment', o.id INTO v_kind, v_id FROM hazem.service_orders o JOIN hazem.payments p ON p.service_order_id = o.id
     WHERE o.person_id = p_person AND o.state = 'awaiting_payment' AND p.state IN ('requested', 'partial', 'not_received')
     ORDER BY o.price_until LIMIT 1;
  END IF;
  IF v_id IS NULL THEN
    SELECT 'booking_payment', a.id INTO v_kind, v_id FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
     WHERE a.person_id = p_person AND a.state = 'confirmed' AND a.ends_at > now() AND p.state IN ('partial', 'not_received', 'requested')
     ORDER BY (p.state = 'requested'), a.starts_at LIMIT 1;
  END IF;
  IF v_id IS NULL THEN
    SELECT 'late_hold', a.id INTO v_kind, v_id FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
     WHERE a.person_id = p_person AND a.state = 'expired' AND a.starts_at > now() AND p.state = 'requested'
     ORDER BY a.updated_at DESC LIMIT 1;
  END IF;

  IF p_image THEN
    IF v_kind IN ('hold', 'order_payment', 'booking_payment') THEN
      RETURN QUERY SELECT v_kind, v_id;
    ELSIF v_urf IS NOT NULL THEN
      RETURN QUERY SELECT 'urf_documents'::text, v_urf;
    ELSIF v_docs IS NOT NULL THEN
      RETURN QUERY SELECT 'order_documents'::text, v_docs;
    ELSIF v_id IS NOT NULL THEN
      RETURN QUERY SELECT v_kind, v_id;
    END IF;
  ELSIF v_urf IS NOT NULL THEN
    RETURN QUERY SELECT 'urf_documents'::text, v_urf;
  ELSIF v_docs IS NOT NULL THEN
    RETURN QUERY SELECT 'order_documents'::text, v_docs;
  ELSIF v_id IS NOT NULL THEN
    RETURN QUERY SELECT 'ask_image'::text, v_id;
  END IF;
END $$;

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

DROP FUNCTION IF EXISTS hazem.retime_urgent(uuid, timestamptz, timestamptz, int);
DROP INDEX IF EXISTS hazem.appointments_urgent_idx;
ALTER TABLE hazem.appointments
  DROP COLUMN IF EXISTS urgent_alerted_at,
  DROP COLUMN IF EXISTS urgent_alert_receipt,
  DROP COLUMN IF EXISTS urgent_acked_at,
  DROP COLUMN IF EXISTS urgent_escalated_at,
  DROP COLUMN IF EXISTS urgent_waiting_at;
ALTER TABLE hazem.payment_receipts
  DROP COLUMN IF EXISTS urgent_decision,
  DROP COLUMN IF EXISTS urgent_decided_at;

DELETE FROM hazem.panel_alerts WHERE task_id IN (SELECT id FROM hazem.staff_tasks WHERE type IN ('urgent_call_failed', 'urgent_call_unanswered'));
DELETE FROM hazem.staff_tasks WHERE type IN ('urgent_call_failed', 'urgent_call_unanswered');
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
  'rating_low'));

DELETE FROM hazem.settings WHERE key IN ('office.lawyer_whatsapp', 'office.manager_whatsapp', 'urgent.call_within_minutes',
  'urgent.escalate_after_minutes');
