-- 0016_urf_cases: TASK 11 (DECISIONS v1.15, B-11 / S-06): the Urfi paths from WhatsApp.
--   hazem.urf_cases   S-06: a new Urfi marriage contract or an Urfi divorce, from Hazem's questions (one per message,
--                     in B-11's order) to the lawyer's review of the documents before the appointment. Every decision
--                     (the age, the guardian, the lawyer's) is kept with its policy_version.
--   hazem.urf_files   the documents (ID cards, the divorce paper, the contract's copy): where they are in storage
--                     (office-sensitive), never their content, never to the AI (T-09).
--   The appointment is a booking (B-08, the hold until the receipt) of one of four in-office services: the contract
--   or the divorce, with the office's witnesses (5,000) or the client's (4,000); its length is booking.urf_minutes.
--   hazem.review_urf_case  the lawyer (or the manager) approves (the confirmation with who must come) or refuses
--                     (the appointment cancelled by the office, everything paid back, B-22).

-- migrate:up
CREATE TABLE hazem.urf_cases (
  id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  person_id      uuid NOT NULL REFERENCES hazem.persons (id),
  intake_id      uuid,
  kind           text NOT NULL CHECK (kind IN ('marriage', 'divorce')),
  state          text NOT NULL DEFAULT 'collecting' CHECK (state IN (
                   'collecting', 'rejected_age', 'rejected_guardian', 'redirected_consultation',
                   'documents_pending', 'booked', 'lawyer_rejected', 'approved')),
  step           text CHECK (step IN ('ages', 'wife_past', 'guardian', 'witnesses', 'has_copy', 'agreed', 'children')),
  husband_age    int CHECK (husband_age BETWEEN 1 AND 120),
  wife_age       int CHECK (wife_age BETWEEN 1 AND 120),
  wife_past      text CHECK (wife_past IN ('first', 'divorced', 'khul')),
  guardian       text CHECK (guardian IN ('father', 'brother', 'uncle', 'none')),
  witnesses      text CHECK (witnesses IN ('office', 'own')),
  has_copy       boolean,
  agreed         boolean,
  children       boolean,
  age_tries      int NOT NULL DEFAULT 0,
  appointment_id uuid REFERENCES hazem.appointments (id),
  policy_version text,                         -- B-11: the rules a decision was taken under
  decided_by     uuid REFERENCES hazem.panel_users (id) ON DELETE SET NULL,
  decided_at     timestamptz,
  client_notice  text CHECK (client_notice IN ('pending', 'sent', 'failed')),
  notice_kind    text CHECK (notice_kind IN ('approved', 'rejected')),
  docs_task_at   timestamptz,                  -- the 24-hour «no documents yet» task, once
  created_at     timestamptz NOT NULL DEFAULT now(),
  updated_at     timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX urf_cases_one_open ON hazem.urf_cases (person_id, kind) WHERE state IN ('collecting', 'documents_pending', 'booked');
CREATE INDEX urf_cases_state_idx ON hazem.urf_cases (state, created_at);
CREATE INDEX urf_cases_notice_idx ON hazem.urf_cases (updated_at) WHERE client_notice = 'pending';
CREATE TRIGGER trg_urf_cases_updated_at BEFORE UPDATE ON hazem.urf_cases
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

CREATE TABLE hazem.urf_files (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  case_id     uuid NOT NULL REFERENCES hazem.urf_cases (id),
  wamid       text NOT NULL UNIQUE,
  bucket      text NOT NULL,
  key         text NOT NULL,
  mime        text,
  filename    text,
  received_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX urf_files_case_idx ON hazem.urf_files (case_id, received_at);

ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_service_check;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_service_check CHECK (service IN (
  'consultation_phone_video', 'consultation_office', 'consultation_urgent',
  'urf_marriage_office', 'urf_marriage_own', 'urf_divorce_office', 'urf_divorce_own'));

ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help'));

ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation', 'booking', 'appointment', 'payment', 'service', 'urf'));

INSERT INTO hazem.settings (key, value, description) VALUES
  ('booking.urf_minutes', '60', 'B-11 (1.15): how long an Urfi appointment in the office takes')
ON CONFLICT (key) DO NOTHING;

-- I4 (1.15): what a file from this client is for, as in 0015, and the documents of an Urfi case: after the
-- receipts (an image while a payment waits is its receipt), before a service order's documents.
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

-- B-11 (1.15): a document for the client's Urfi case. first = the case's first one (Hazem answers, and the lawyer
-- gets a task once).
CREATE FUNCTION hazem.add_urf_file(p_person uuid, p_wamid text, p_bucket text, p_key text, p_mime text, p_filename text)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.urf_cases;
  v_first boolean;
BEGIN
  IF EXISTS (SELECT 1 FROM hazem.urf_files WHERE wamid = p_wamid) THEN
    RETURN jsonb_build_object('status', 'exists');
  END IF;
  SELECT c.* INTO v FROM hazem.urf_cases c
   WHERE c.id = (SELECT t.id FROM hazem.file_target(p_person, p_mime LIKE 'image/%') t WHERE t.kind = 'urf_documents') FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'none');
  END IF;
  v_first := NOT EXISTS (SELECT 1 FROM hazem.urf_files WHERE case_id = v.id);
  INSERT INTO hazem.urf_files (case_id, wamid, bucket, key, mime, filename) VALUES (v.id, p_wamid, p_bucket, p_key, p_mime, p_filename);
  RETURN jsonb_build_object('status', CASE WHEN v_first THEN 'first' ELSE 'more' END, 'id', v.id);
END $$;

-- B-05 / S-04 (1.15): as in 0015, and a receipt reviewed after the lawyer refused the Urfi case opens its refund.
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

-- B-11.6 / B-22 (1.15): the lawyer's review before the appointment. approved → the confirmation (step 7) for the
-- worker (a confirmed appointment only); rejected → the appointment cancelled by the office without B-22's «another time»
-- list, its event removed, and the money a person found due back in full. Returns the refund, for the office's task.
CREATE FUNCTION hazem.review_urf_case(p_case uuid, p_decision text, p_by uuid, p_policy text)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.urf_cases;
  v_state text;
  v_refund int;
BEGIN
  SELECT * INTO v FROM hazem.urf_cases WHERE id = p_case FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF p_decision NOT IN ('approved', 'rejected') THEN
    RETURN jsonb_build_object('status', 'invalid');
  END IF;
  SELECT state INTO v_state FROM hazem.appointments WHERE id = v.appointment_id AND ends_at > now() FOR UPDATE;
  -- Approved: only a confirmed appointment (the receipt came), never a hold that may still run out.
  IF p_decision = 'approved' AND NOT (v.state = 'booked' AND v_state IS NOT DISTINCT FROM 'confirmed')
     OR p_decision = 'rejected' AND v.state NOT IN ('documents_pending', 'booked') THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;

  UPDATE hazem.urf_cases
     SET state = CASE p_decision WHEN 'approved' THEN 'approved' ELSE 'lawyer_rejected' END,
         decided_by = p_by, decided_at = now(), policy_version = p_policy,
         client_notice = 'pending', notice_kind = p_decision
   WHERE id = v.id;
  IF p_decision = 'rejected' AND v_state IN ('pending_payment', 'confirmed') THEN
    UPDATE hazem.appointments
       SET state = 'cancelled_by_office', client_notice = NULL, hold_until = NULL,
           calendar_state = CASE WHEN calendar_event_id IS NULL THEN 'removed' ELSE 'remove' END,
           calendar_attempts = 0, calendar_next_at = now()
     WHERE id = v.appointment_id;
    -- Only money a person found is due back now; a receipt nobody checked yet stays for the review, and
    -- mark_payment opens the refund if the money did come (as for a closed service order, 0015).
    UPDATE hazem.payments
       SET state = 'refund_requested', refund_due = amount_received, refund_reason = 'office_cancelled'
     WHERE appointment_id = v.appointment_id AND state IN ('verified', 'partial') AND amount_received > 0
    RETURNING refund_due INTO v_refund;
  END IF;
  RETURN jsonb_build_object('status', p_decision, 'refund', v_refund, 'person', v.person_id, 'appointment', v.appointment_id);
END $$;

-- B-11 (1.15), run by the worker: a booked case whose time is gone goes back to picking one; a booked case whose
-- appointment is within 24 hours and still has no document
-- gives the office a task, once. Returns those cases.
CREATE FUNCTION hazem.urf_due()
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v jsonb;
BEGIN
  -- A booked case whose time is gone (the hold ran out, the client cancelled, the office cancelled a day) goes back to
  -- picking a time: it no longer takes files, and «عايز جواز عرفي» again offers the times.
  UPDATE hazem.urf_cases c SET state = 'documents_pending', appointment_id = NULL
   WHERE c.state = 'booked'
     AND NOT EXISTS (SELECT 1 FROM hazem.appointments a WHERE a.id = c.appointment_id AND a.state IN ('pending_payment', 'confirmed'));
  WITH due AS (
    UPDATE hazem.urf_cases c SET docs_task_at = now()
      FROM hazem.appointments a
     WHERE a.id = c.appointment_id AND c.state = 'booked' AND c.docs_task_at IS NULL
       AND a.state IN ('pending_payment', 'confirmed') AND a.starts_at <= now() + interval '24 hours' AND a.starts_at > now()
       AND NOT EXISTS (SELECT 1 FROM hazem.urf_files f WHERE f.case_id = c.id)
    RETURNING c.id, c.person_id)
  SELECT COALESCE(jsonb_agg(jsonb_build_object('id', id, 'person', person_id)), '[]'::jsonb) INTO v FROM due;
  RETURN v;
END $$;

-- The texts. Word for word from DECISIONS B-11, and the ones proposed in TASK 11 (approved by the owner the same day,
-- «موافق على نصوص وزراير العرفي الجديدة»). Without them a Urfi request is answered as before (the text and a task).
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_ask_ages', 1, 'سن الزوج كام، وسن الزوجة كام؟', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_under_age', 1, 'نعتذر لحضرتك، المكتب ما بيعملش عقود جواز لحد أقل من 18 سنة', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_ask_wife_past', 1, 'دي أول جوازة للزوجة، ولا سبق لها الجواز واتطلقت أو اختلعت؟', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_ask_guardian', 1, 'مين من أهل الزوجة موجود وموافق: الأب، ولا الأخ، ولا العم؟', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_no_guardian', 1, 'نعتذر لحضرتك، دي سياسة المكتب في عقود الجواز العرفي', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_ask_witnesses', 1, 'الشهود هتجيبهم حضرتك، ولا المكتب يوفرهم؟', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_ask_copy', 1, 'معاكم نسخة من العقد العرفي؟', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_ask_agreed', 1, 'الطرفين متفقين على الطلاق؟', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_ask_children', 1, 'في أطفال من الجواز ده؟', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_confirmed', 1, 'تم تأكيد الميعاد يوم {{day}} الساعة {{time}} في المكتب ✅. الحضور: {{attendees}}، ومعاكم أصول البطاقات.', 300, true, 'DECISIONS v1.15 B-11', 'migration 0016'),
  ('urf_ages_again', 1, 'من فضلك اكتبلي السن بالأرقام، الزوج الأول وبعده الزوجة. مثلًا: 30 و 25', 300, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('urf_choose_slot', 1, '{{service}}: {{amount}} جنيه، والميعاد في المكتب.

المستندات اللي هنحتاجها: {{documents}}.

اختار ميعاد من القايمة 👇', 600, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('urf_send_documents', 1, '📎 دلوقتي ابعت هنا صور المستندات: {{documents}}. الأستاذ هيراجعها قبل الميعاد.', 600, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('urf_documents_received', 1, 'استلمت ✅. الأستاذ هيراجع الورق قبل الميعاد، ولو محتاج حاجة هنبلّغك هنا.', 300, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('urf_rejected', 1, 'نعتذر لحضرتك، الأستاذ راجع الورق ومش هيقدر يكمّل الطلب ده، والميعاد اتلغى. لو كنت حوّلت فلوس هترجعلك كاملة، وهنبلّغك هنا أول ما تتحوّل.', 300, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_first', 1, 'أول جوازة', 20, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_divorced', 1, 'اتطلقت قبل كده', 20, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_khul', 1, 'اختلعت قبل كده', 20, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_guardian_list', 1, 'اختار', 20, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_father', 1, 'الأب', 24, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_brother', 1, 'الأخ', 24, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_uncle', 1, 'العم', 24, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_no_guardian', 1, 'مفيش حد منهم', 24, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_witness_office', 1, 'المكتب يوفرهم', 20, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_urf_witness_own', 1, 'هنجيبهم إحنا', 20, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_yes', 1, 'أيوه', 20, true, 'proposal TASK 11', 'owner approval 2026-10-24'),
  ('btn_no', 1, 'لأ', 20, true, 'proposal TASK 11', 'owner approval 2026-10-24')
ON CONFLICT (key, version) DO NOTHING;

GRANT EXECUTE ON FUNCTION hazem.add_urf_file(uuid, text, text, text, text, text), hazem.review_urf_case(uuid, text, uuid, text),
  hazem.urf_due() TO office_api;
GRANT SELECT ON hazem.urf_cases, hazem.urf_files TO office_readonly;
REVOKE ALL ON hazem.urf_cases, hazem.urf_files FROM office_app;
REVOKE ALL ON FUNCTION hazem.add_urf_file(uuid, text, text, text, text, text), hazem.review_urf_case(uuid, text, uuid, text),
  hazem.urf_due() FROM PUBLIC;

-- migrate:down
DROP FUNCTION IF EXISTS hazem.urf_due();
DROP FUNCTION IF EXISTS hazem.review_urf_case(uuid, text, uuid, text);
DROP FUNCTION IF EXISTS hazem.add_urf_file(uuid, text, text, text, text, text);

-- 0015's mark_payment, as it was.
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

  -- An order the office closed before anyone checked its receipt: whatever came is due back (B-22).
  v_closed := p_state IN ('verified', 'partial')
              AND EXISTS (SELECT 1 FROM hazem.service_orders WHERE id = v.service_order_id AND state = 'closed');
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

-- 0015's file_target, as it was.
CREATE OR REPLACE FUNCTION hazem.file_target(p_person uuid, p_image boolean)
RETURNS TABLE (kind text, id uuid) LANGUAGE plpgsql STABLE SET search_path = hazem, pg_temp AS $$
DECLARE
  v_docs uuid;
  v_kind text;
  v_id uuid;
BEGIN
  SELECT o.id INTO v_docs FROM hazem.service_orders o
   WHERE o.person_id = p_person AND o.state IN ('collecting', 'awaiting_price') ORDER BY o.created_at DESC LIMIT 1;

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
    ELSIF v_docs IS NOT NULL THEN
      RETURN QUERY SELECT 'order_documents'::text, v_docs;
    ELSIF v_id IS NOT NULL THEN
      RETURN QUERY SELECT v_kind, v_id;
    END IF;
  ELSIF v_docs IS NOT NULL THEN
    RETURN QUERY SELECT 'order_documents'::text, v_docs;
  ELSIF v_id IS NOT NULL THEN
    RETURN QUERY SELECT 'ask_image'::text, v_id;
  END IF;
END $$;

UPDATE hazem.approved_texts SET active = false WHERE source IN ('proposal TASK 11', 'DECISIONS v1.15 B-11');
DELETE FROM hazem.approved_texts t WHERE t.source IN ('proposal TASK 11', 'DECISIONS v1.15 B-11')
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
DELETE FROM hazem.settings WHERE key = 'booking.urf_minutes';

DELETE FROM hazem.panel_audit WHERE target_type = 'urf';
ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation', 'booking', 'appointment', 'payment', 'service'));
DELETE FROM hazem.staff_tasks WHERE type IN ('urf_review', 'urf_documents_missing', 'urf_help');
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid'));

DROP TABLE IF EXISTS hazem.urf_files;
DROP TABLE IF EXISTS hazem.urf_cases;
DELETE FROM hazem.payment_receipts WHERE payment_id IN (
  SELECT p.id FROM hazem.payments p JOIN hazem.appointments a ON a.id = p.appointment_id WHERE a.service LIKE 'urf_%');
DELETE FROM hazem.payments WHERE appointment_id IN (SELECT id FROM hazem.appointments WHERE service LIKE 'urf_%');
DELETE FROM hazem.appointments WHERE service LIKE 'urf_%';
ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_service_check;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_service_check
  CHECK (service IN ('consultation_phone_video', 'consultation_office', 'consultation_urgent'));
