-- 0015_service_orders: TASK 10 (DECISIONS v1.14, B-16 / B-17 / S-07).
--   hazem.service_orders  S-07: a written service from the client's request to its delivery: the documents,
--                         the price and the delivery day set by the lawyer or the manager, the payment
--                         (hazem.payments, B-05 as for bookings), the work once a person verified the
--                         transfer, and the file sent on WhatsApp. One open order per client and kind.
--   hazem.service_files   the client's documents for an order: where they are in storage, never their content.
--   hazem.file_target     the one rule for what a file from the client is for (I4): a hold waiting for its
--                         receipt, an order waiting for payment, a confirmed booking waiting for a (new)
--                         receipt, an order waiting for documents, a hold that ran out. A receipt is an image
--                         only (P7); a PDF or Word goes to an order waiting for documents.
-- The delivered file is put in office-sensitive by the panel (a signed upload through the panel's nginx); the
-- API only checks it is there and records where (v1.3: the API never receives a file).

-- migrate:up
CREATE TABLE hazem.service_orders (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  person_id         uuid NOT NULL REFERENCES hazem.persons (id),
  intake_id         uuid,
  kind              text NOT NULL CHECK (kind IN ('document_review', 'contract_drafting', 'formal_notice', 'written_opinion')),
  state             text NOT NULL DEFAULT 'collecting'
                      CHECK (state IN ('collecting', 'awaiting_price', 'awaiting_payment', 'in_progress', 'delivered', 'closed')),
  appointment_id    uuid REFERENCES hazem.appointments (id),  -- B-17: the consultation a written opinion follows
  opened_key        text NOT NULL UNIQUE,                      -- the turn (or the panel action) that opened it
  price             int CHECK (price > 0),
  price_exception   boolean NOT NULL DEFAULT false,            -- B-10: outside the range, confirmed by the office
  due_on            date,
  delivery          text CHECK (delivery IN ('file', 'call')),
  priced_by         uuid REFERENCES hazem.panel_users (id) ON DELETE SET NULL,
  priced_at         timestamptz,
  price_until       timestamptz,                               -- v1.14: the price holds 3 days
  price_reminded_at timestamptz,
  file_bucket       text,
  file_key          text,
  file_name         text CHECK (char_length(file_name) <= 120),
  file_mime         text,
  delivered_by      uuid REFERENCES hazem.panel_users (id) ON DELETE SET NULL,
  delivered_at      timestamptz,
  closed_reason     text CHECK (closed_reason IN ('price_expired', 'office')),
  client_notice     text CHECK (client_notice IN ('pending', 'sent', 'failed')),
  notice_kind       text CHECK (notice_kind IN ('priced', 'price_reminder', 'price_expired', 'started', 'delivered', 'closed')),
  notice_attempt    int NOT NULL DEFAULT 0,                    -- a notice sent again (the window reopened, «ابعت تاني»)
  price_expired_at  timestamptz,                               -- the price ran out with part of the money in: the office decides
  created_at        timestamptz NOT NULL DEFAULT now(),
  updated_at        timestamptz NOT NULL DEFAULT now(),
  CHECK (file_key IS NULL OR file_bucket = 'office-sensitive' AND file_key LIKE 'deliverables/%')
);
CREATE UNIQUE INDEX service_orders_one_open ON hazem.service_orders (person_id, kind) WHERE state NOT IN ('delivered', 'closed');
CREATE INDEX service_orders_state_idx ON hazem.service_orders (state, created_at);
CREATE INDEX service_orders_notice_idx ON hazem.service_orders (updated_at) WHERE client_notice = 'pending';
CREATE TRIGGER trg_service_orders_updated_at BEFORE UPDATE ON hazem.service_orders
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

CREATE TABLE hazem.service_files (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  order_id    uuid NOT NULL REFERENCES hazem.service_orders (id),
  wamid       text NOT NULL UNIQUE,
  bucket      text NOT NULL,
  key         text NOT NULL,
  mime        text,
  filename    text,
  received_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX service_files_order_idx ON hazem.service_files (order_id, received_at);

-- A payment is for a booking or for a service order, never both.
ALTER TABLE hazem.payments ALTER COLUMN appointment_id DROP NOT NULL;
ALTER TABLE hazem.payments ADD COLUMN service_order_id uuid UNIQUE REFERENCES hazem.service_orders (id);
ALTER TABLE hazem.payments ADD CONSTRAINT payments_one_owner CHECK ((appointment_id IS NULL) <> (service_order_id IS NULL));

ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid'));

ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation', 'booking', 'appointment', 'payment', 'service'));

-- I4: what a file from this client is for. kind: hold, order_payment, booking_payment, order_documents,
-- late_hold, or (a PDF or Word while a payment waits) ask_image. No row: nothing waits for a file.
CREATE FUNCTION hazem.file_target(p_person uuid, p_image boolean)
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

-- B-05 (1.13, 1.14): a receipt image, for hazem.file_target: a hold → confirmed; an order → its payment's
-- receipt (the work starts after a person's review); a confirmed booking → its new receipt; a hold whose time
-- ran out → confirmed again when the time is still free (p_late_free), otherwise kept as a credit.
CREATE OR REPLACE FUNCTION hazem.attach_receipt(
  p_person uuid, p_wamid text, p_bucket text, p_key text, p_sha256 text, p_mime text, p_late_free boolean, p_buffer_minutes int)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_a hazem.appointments;
  v_p hazem.payments;
  v_kind text;
  v_id uuid;
  v_status text;
  v_dup uuid;
  v_buffer interval := make_interval(mins => GREATEST(p_buffer_minutes, 0));
BEGIN
  SELECT p.* INTO v_p FROM hazem.payment_receipts r JOIN hazem.payments p ON p.id = r.payment_id WHERE r.wamid = p_wamid;
  IF FOUND THEN -- the same message again
    IF v_p.service_order_id IS NOT NULL THEN
      RETURN jsonb_build_object('status', 'exists', 'order', v_p.service_order_id,
                                'kind', (SELECT kind FROM hazem.service_orders WHERE id = v_p.service_order_id));
    END IF;
    RETURN jsonb_build_object('status', 'exists', 'id', v_p.appointment_id,
                              'state', (SELECT state FROM hazem.appointments WHERE id = v_p.appointment_id));
  END IF;

  PERFORM pg_advisory_xact_lock(hashtext('hazem.appointments'));
  SELECT t.kind, t.id INTO v_kind, v_id FROM hazem.file_target(p_person, true) t;
  IF v_kind IS NULL OR v_kind NOT IN ('hold', 'order_payment', 'booking_payment', 'late_hold') THEN
    RETURN jsonb_build_object('status', 'none');
  END IF;

  IF v_kind = 'order_payment' THEN
    SELECT * INTO v_p FROM hazem.payments WHERE service_order_id = v_id FOR UPDATE;
    v_status := 'order_received';
  ELSE
    SELECT * INTO v_a FROM hazem.appointments WHERE id = v_id FOR UPDATE;
    IF v_kind = 'hold' THEN
      -- Still held (even a moment after its time, before the worker freed it): nobody else has it.
      UPDATE hazem.appointments
         SET state = 'confirmed', calendar_state = 'create', calendar_attempts = 0, calendar_next_at = now(),
             client_notice = CASE WHEN notice_kind = 'hold_reminder' AND client_notice = 'pending' THEN NULL ELSE client_notice END
       WHERE id = v_a.id;
      v_status := 'confirmed';
    ELSIF v_kind = 'booking_payment' THEN
      v_status := 'received';
    ELSIF p_late_free AND NOT EXISTS (SELECT 1 FROM hazem.appointments
                                       WHERE id <> v_a.id AND state IN ('confirmed', 'pending_payment')
                                         AND tstzrange(starts_at - v_buffer, ends_at + v_buffer) && tstzrange(v_a.starts_at, v_a.ends_at)) THEN
      UPDATE hazem.appointments
         SET state = 'confirmed', calendar_state = 'create', calendar_attempts = 0, calendar_next_at = now(), client_notice = NULL
       WHERE id = v_a.id;
      v_status := 'confirmed_late';
    ELSE
      v_status := 'late_taken';
    END IF;

    SELECT * INTO v_p FROM hazem.payments WHERE appointment_id = v_a.id FOR UPDATE;
    IF NOT FOUND THEN
      INSERT INTO hazem.payments (appointment_id, amount_due) VALUES (v_a.id, v_a.amount) RETURNING * INTO v_p;
    END IF;
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

  IF v_kind = 'order_payment' THEN
    RETURN jsonb_build_object('status', v_status, 'order', v_id, 'duplicate', v_dup IS NOT NULL,
                              'kind', (SELECT kind FROM hazem.service_orders WHERE id = v_id));
  END IF;
  -- P6: the calendar event shows the review; a new receipt means «waiting for the review» again.
  UPDATE hazem.appointments SET calendar_state = 'update', calendar_attempts = 0, calendar_next_at = now()
   WHERE id = v_a.id AND calendar_state = 'synced';
  RETURN jsonb_build_object('status', v_status, 'id', v_a.id, 'duplicate', v_dup IS NOT NULL);
END $$;
DROP FUNCTION hazem.receipt_target(uuid);

-- B-05 / S-04 (1.14): as in 0014, and for an order: «تمام» (or more than due) starts the work (in_progress) and
-- the client hears it; partial and not received are told while the order still waits for its payment.
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

-- B-22 (P2), as in 0014, for a booking's payment or an order's.
CREATE OR REPLACE FUNCTION hazem.mark_refunded(p_appointment uuid, p_by uuid, p_method text, p_note text)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.payments;
BEGIN
  SELECT * INTO v FROM hazem.payments WHERE appointment_id = p_appointment OR service_order_id = p_appointment FOR UPDATE;
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

-- B-16.1: an order for a service named by the client, or the one already open for that kind.
CREATE FUNCTION hazem.open_service_order(p_person uuid, p_intake uuid, p_kind text, p_key text)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.service_orders;
BEGIN
  -- Two turns of the same client at once must not both insert (service_orders_one_open).
  PERFORM pg_advisory_xact_lock(hashtext('hazem.service_orders:' || p_person));
  SELECT * INTO v FROM hazem.service_orders WHERE opened_key = p_key;
  IF FOUND THEN
    RETURN jsonb_build_object('status', 'exists', 'id', v.id, 'state', v.state);
  END IF;
  SELECT * INTO v FROM hazem.service_orders WHERE person_id = p_person AND kind = p_kind AND state NOT IN ('delivered', 'closed');
  IF FOUND THEN
    RETURN jsonb_build_object('status', 'open', 'id', v.id, 'state', v.state);
  END IF;
  INSERT INTO hazem.service_orders (person_id, intake_id, kind, opened_key) VALUES (p_person, p_intake, p_kind, p_key) RETURNING * INTO v;
  RETURN jsonb_build_object('status', 'opened', 'id', v.id, 'state', v.state);
END $$;

-- B-16.2: a document from the client for his order waiting for documents (or its price). first = the first
-- one (Hazem answers and the office gets a task once).
CREATE FUNCTION hazem.add_service_file(p_person uuid, p_wamid text, p_bucket text, p_key text, p_mime text, p_filename text)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.service_orders;
  v_first boolean;
BEGIN
  IF EXISTS (SELECT 1 FROM hazem.service_files WHERE wamid = p_wamid) THEN
    RETURN jsonb_build_object('status', 'exists');
  END IF;
  SELECT o.* INTO v FROM hazem.service_orders o
   WHERE o.id = (SELECT t.id FROM hazem.file_target(p_person, false) t WHERE t.kind = 'order_documents') FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'none');
  END IF;
  v_first := v.state = 'collecting';
  INSERT INTO hazem.service_files (order_id, wamid, bucket, key, mime, filename) VALUES (v.id, p_wamid, p_bucket, p_key, p_mime, p_filename);
  UPDATE hazem.service_orders SET state = 'awaiting_price' WHERE id = v.id AND state = 'collecting';
  RETURN jsonb_build_object('status', CASE WHEN v_first THEN 'first' ELSE 'more' END, 'id', v.id, 'kind', v.kind);
END $$;

-- B-16.3 (B-10, v1.14): the lawyer or the manager prices it; the price holds 3 days.
CREATE FUNCTION hazem.price_service_order(p_order uuid, p_price int, p_exception boolean, p_due_on date, p_delivery text, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.service_orders;
  v_in_range boolean;
BEGIN
  SELECT * INTO v FROM hazem.service_orders WHERE id = p_order FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v.state NOT IN ('collecting', 'awaiting_price') THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  IF p_price IS NULL OR p_price <= 0 OR p_price > 1000000 OR p_due_on IS NULL OR p_due_on < current_date
     OR p_delivery NOT IN ('file', 'call') THEN
    RETURN jsonb_build_object('status', 'invalid');
  END IF;
  v_in_range := CASE v.kind
                  WHEN 'document_review' THEN p_price BETWEEN 1500 AND 3000
                  WHEN 'contract_drafting' THEN p_price >= 3000
                  ELSE p_price = 2000 END;
  IF NOT v_in_range AND NOT p_exception THEN
    RETURN jsonb_build_object('status', 'out_of_range');
  END IF;

  UPDATE hazem.service_orders
     SET state = 'awaiting_payment', price = p_price, price_exception = NOT v_in_range, due_on = p_due_on, delivery = p_delivery,
         priced_by = p_by, priced_at = now(), price_until = now() + interval '3 days', client_notice = 'pending', notice_kind = 'priced'
   WHERE id = v.id;
  INSERT INTO hazem.payments (service_order_id, amount_due) VALUES (v.id, p_price);
  RETURN jsonb_build_object('status', 'priced');
END $$;

-- B-17 (1.14): «مطلوب رأي مكتوب» on a consultation that took place: an order waiting for its 2,000 at once.
CREATE FUNCTION hazem.open_written_opinion(p_appointment uuid, p_due_on date, p_delivery text, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_a hazem.appointments;
  v_id uuid;
BEGIN
  SELECT * INTO v_a FROM hazem.appointments WHERE id = p_appointment;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v_a.state <> 'completed' THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  IF p_due_on IS NULL OR p_due_on < current_date OR p_delivery NOT IN ('file', 'call') THEN
    RETURN jsonb_build_object('status', 'invalid');
  END IF;
  IF EXISTS (SELECT 1 FROM hazem.service_orders WHERE opened_key = 'opinion:' || p_appointment)
     OR EXISTS (SELECT 1 FROM hazem.service_orders WHERE person_id = v_a.person_id AND kind = 'written_opinion' AND state NOT IN ('delivered', 'closed')) THEN
    RETURN jsonb_build_object('status', 'exists');
  END IF;
  INSERT INTO hazem.service_orders (person_id, intake_id, kind, state, appointment_id, opened_key, price, due_on, delivery,
                                    priced_by, priced_at, price_until, client_notice, notice_kind)
  VALUES (v_a.person_id, v_a.intake_id, 'written_opinion', 'awaiting_payment', v_a.id, 'opinion:' || p_appointment, 2000, p_due_on, p_delivery,
          p_by, now(), now() + interval '3 days', 'pending', 'priced')
  RETURNING id INTO v_id;
  INSERT INTO hazem.payments (service_order_id, amount_due) VALUES (v_id, 2000);
  RETURN jsonb_build_object('status', 'opened', 'id', v_id);
END $$;

-- v1.14, run by the worker: one reminder a day before the price runs out (no receipt yet), then the order
-- closes (the office gets a task, the client a word): nothing came, or what came was marked «ما وصلش».
-- Part of the money in: the order stays for the office to decide (a task, once). A receipt still waiting for
-- its review keeps the order as it is. Returns the orders that ran out, for their tasks.
CREATE FUNCTION hazem.service_due()
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_closed jsonb;
  v_partial jsonb;
BEGIN
  UPDATE hazem.service_orders o
     SET price_reminded_at = now(), client_notice = 'pending', notice_kind = 'price_reminder'
    FROM hazem.payments p
   WHERE p.service_order_id = o.id AND p.state = 'requested'
     AND o.state = 'awaiting_payment' AND o.price_reminded_at IS NULL
     AND o.price_until - interval '1 day' <= now() AND o.price_until > now();
  WITH closed AS (
    UPDATE hazem.service_orders o
       SET state = 'closed', closed_reason = 'price_expired', client_notice = 'pending', notice_kind = 'price_expired'
      FROM hazem.payments p
     WHERE p.service_order_id = o.id AND p.state IN ('requested', 'not_received')
       AND o.state = 'awaiting_payment' AND o.price_until <= now()
    RETURNING o.id, o.person_id)
  SELECT COALESCE(jsonb_agg(jsonb_build_object('id', id, 'person', person_id)), '[]'::jsonb) INTO v_closed FROM closed;
  WITH partial AS (
    UPDATE hazem.service_orders o SET price_expired_at = now()
      FROM hazem.payments p
     WHERE p.service_order_id = o.id AND p.state = 'partial'
       AND o.state = 'awaiting_payment' AND o.price_until <= now() AND o.price_expired_at IS NULL
    RETURNING o.id, o.person_id)
  SELECT COALESCE(jsonb_agg(jsonb_build_object('id', id, 'person', person_id)), '[]'::jsonb) INTO v_partial FROM partial;
  RETURN v_closed || v_partial;
END $$;

-- B-16.6: delivered: the file the panel put in office-sensitive (the API checked it is there), or the call made.
CREATE FUNCTION hazem.deliver_service_order(p_order uuid, p_by uuid, p_key text, p_name text, p_mime text)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.service_orders;
BEGIN
  SELECT * INTO v FROM hazem.service_orders WHERE id = p_order FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v.state <> 'in_progress' THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  IF v.delivery = 'file' AND (p_key IS NULL OR p_key NOT LIKE 'deliverables/' || p_order || '/%') THEN
    RETURN jsonb_build_object('status', 'invalid');
  END IF;
  UPDATE hazem.service_orders
     SET state = 'delivered', delivered_by = p_by, delivered_at = now(),
         file_bucket = CASE WHEN v.delivery = 'file' THEN 'office-sensitive' END,
         file_key = CASE WHEN v.delivery = 'file' THEN p_key END,
         file_name = CASE WHEN v.delivery = 'file' THEN left(p_name, 120) END,
         file_mime = CASE WHEN v.delivery = 'file' THEN p_mime END,
         client_notice = 'pending', notice_kind = 'delivered'  -- the file with its caption, or the words after the call
   WHERE id = v.id;
  RETURN jsonb_build_object('status', 'delivered');
END $$;

-- B-16.6 (24-hour window): a delivered file sent again from the panel («ابعت الملف تاني»), with a new key.
CREATE FUNCTION hazem.resend_service_file(p_order uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.service_orders;
BEGIN
  SELECT * INTO v FROM hazem.service_orders WHERE id = p_order FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v.state <> 'delivered' OR v.file_key IS NULL OR v.client_notice = 'pending' THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  UPDATE hazem.service_orders SET client_notice = 'pending', notice_kind = 'delivered', notice_attempt = notice_attempt + 1
   WHERE id = v.id;
  RETURN jsonb_build_object('status', 'resent');
END $$;

-- 24-hour window: a message from the client opens it again, so what could not reach him is sent now
-- (the price while it waits for payment, the work started, the file), each with a new key.
CREATE FUNCTION hazem.retry_service_notices(p_person uuid)
RETURNS int LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_count int;
BEGIN
  UPDATE hazem.service_orders
     SET client_notice = 'pending', notice_attempt = notice_attempt + 1
   WHERE person_id = p_person AND client_notice = 'failed'
     AND (notice_kind = 'priced' AND state = 'awaiting_payment' AND price_until > now()
          OR notice_kind = 'started' AND state = 'in_progress'
          OR notice_kind = 'delivered' AND state = 'delivered');
  GET DIAGNOSTICS v_count = ROW_COUNT;
  RETURN v_count;
END $$;

-- B-16.7: the office closes an order; money that came is due back (B-22).
CREATE FUNCTION hazem.close_service_order(p_order uuid, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.service_orders;
  v_refund int;
BEGIN
  SELECT * INTO v FROM hazem.service_orders WHERE id = p_order FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v.state IN ('delivered', 'closed') THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  UPDATE hazem.service_orders SET state = 'closed', closed_reason = 'office', client_notice = 'pending', notice_kind = 'closed'
   WHERE id = v.id;
  -- Only money a person found (verified, partial) is due back now; a receipt nobody checked yet stays for the
  -- review, and mark_payment opens the refund if the money did come.
  UPDATE hazem.payments
     SET state = 'refund_requested', refund_due = amount_received, refund_reason = 'office_cancelled'
   WHERE service_order_id = v.id AND state IN ('verified', 'partial') AND amount_received > 0
  RETURNING refund_due INTO v_refund;
  RETURN jsonb_build_object('status', 'closed', 'refund', v_refund, 'person', v.person_id);
END $$;

-- Proposed in TASK 10: inactive until the owner approves them. Until then a service named by the client is
-- answered as before (the text and a task), and the panel can still price and deliver.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('service_files_received', 1, 'استلمت ✅. الأستاذ هيراجع اللي بعته حضرتك ويبلّغك هنا بالسعر النهائي ويوم التسليم.', 300, true, 'proposal TASK 10', 'owner approval 2026-10-24'),
  ('service_priced', 1, '💼 {{service}}: السعر النهائي {{amount}} جنيه، والتسليم يوم {{due}} ({{delivery}}).

عشان نبدأ:
1️⃣ حوّل {{amount}} جنيه على InstaPay: {{instapay}}، أو محفظة كاش: {{wallet}}.
2️⃣ ابعت صورة (سكرين شوت) التحويل هنا في الشات.

⏰ السعر ده صالح لحد {{deadline}}.', 600, true, 'proposal TASK 10', 'owner approval 2026-10-24'),
  ('service_price_reminder', 1, '⏰ تذكير: سعر {{service}} ({{amount}} جنيه) صالح لحد {{deadline}}. بعد التحويل ابعت صورته هنا.', 300, true, 'proposal TASK 10', 'owner approval 2026-10-24'),
  ('service_price_expired', 1, 'طلب {{service}} اتقفل لأن التحويل ما وصلش في الوقت. لو لسه محتاجه، ابعتلنا هنا ونكمّل مع حضرتك.', 300, true, 'proposal TASK 10', 'owner approval 2026-10-24'),
  ('service_receipt_received', 1, 'استلمنا صورة التحويل ✅. الأستاذ هيبدأ في {{service}} أول ما المكتب يراجع التحويل.', 300, true, 'proposal TASK 10', 'owner approval 2026-10-24'),
  ('service_started', 1, '✅ الأستاذ بدأ في {{service}}، والتسليم يوم {{due}}.', 300, true, 'proposal TASK 10', 'owner approval 2026-10-24'),
  ('service_delivered', 1, '📄 {{service}} بتاع حضرتك جاهز ✅', 300, true, 'proposal TASK 10', 'owner approval 2026-10-24'),
  ('service_payment_partial', 1, '⚠️ وصلنا {{received}} جنيه من {{amount}} جنيه لـ{{service}}. محتاجين باقي المبلغ {{rest}} جنيه، وبعد ما تحوّل ابعت صورة التحويل هنا.', 300, true, 'proposal TASK 10', 'owner approval 2026-10-24'),
  ('service_payment_not_received', 1, '⚠️ تحويل {{service}} ما وصلش لحد دلوقتي. ممكن حضرتك تتأكد من التحويل وتبعت صورته تاني هنا؟', 300, true, 'proposal TASK 10', 'owner approval 2026-10-24'),
  ('service_closed', 1, 'طلب {{service}} اتقفل. لو حضرتك كنت حوّلت فلوس هترجعلك، وهنبلّغك هنا أول ما تتحوّل.', 300, true, 'proposal TASK 10', 'owner approval 2026-10-24')
ON CONFLICT (key, version) DO NOTHING;

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.service_orders, hazem.service_files TO office_api;
GRANT EXECUTE ON FUNCTION hazem.file_target(uuid, boolean), hazem.open_service_order(uuid, uuid, text, text),
  hazem.add_service_file(uuid, text, text, text, text, text), hazem.price_service_order(uuid, int, boolean, date, text, uuid),
  hazem.open_written_opinion(uuid, date, text, uuid), hazem.service_due(), hazem.deliver_service_order(uuid, uuid, text, text, text),
  hazem.close_service_order(uuid, uuid), hazem.resend_service_file(uuid), hazem.retry_service_notices(uuid) TO office_api;
GRANT SELECT ON hazem.service_orders, hazem.service_files TO office_readonly;
REVOKE ALL ON hazem.service_orders, hazem.service_files FROM office_app;
REVOKE ALL ON FUNCTION hazem.file_target(uuid, boolean), hazem.open_service_order(uuid, uuid, text, text),
  hazem.add_service_file(uuid, text, text, text, text, text), hazem.price_service_order(uuid, int, boolean, date, text, uuid),
  hazem.open_written_opinion(uuid, date, text, uuid), hazem.service_due(), hazem.deliver_service_order(uuid, uuid, text, text, text),
  hazem.close_service_order(uuid, uuid), hazem.resend_service_file(uuid), hazem.retry_service_notices(uuid) FROM PUBLIC;

-- migrate:down
DROP FUNCTION IF EXISTS hazem.retry_service_notices(uuid);
DROP FUNCTION IF EXISTS hazem.resend_service_file(uuid);
DROP FUNCTION IF EXISTS hazem.close_service_order(uuid, uuid);
DROP FUNCTION IF EXISTS hazem.deliver_service_order(uuid, uuid, text, text, text);
DROP FUNCTION IF EXISTS hazem.service_due();
DROP FUNCTION IF EXISTS hazem.open_written_opinion(uuid, date, text, uuid);
DROP FUNCTION IF EXISTS hazem.price_service_order(uuid, int, boolean, date, text, uuid);
DROP FUNCTION IF EXISTS hazem.add_service_file(uuid, text, text, text, text, text);
DROP FUNCTION IF EXISTS hazem.open_service_order(uuid, uuid, text, text);

UPDATE hazem.approved_texts SET active = false WHERE source = 'proposal TASK 10';
DELETE FROM hazem.approved_texts t WHERE t.source = 'proposal TASK 10'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);

DELETE FROM hazem.panel_audit WHERE target_type = 'service';
ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation', 'booking', 'appointment', 'payment'));
DELETE FROM hazem.staff_tasks WHERE type IN ('service_pricing', 'service_unpaid');
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled'));

DELETE FROM hazem.payment_receipts WHERE payment_id IN (SELECT id FROM hazem.payments WHERE service_order_id IS NOT NULL);
DELETE FROM hazem.payments WHERE service_order_id IS NOT NULL;
ALTER TABLE hazem.payments DROP CONSTRAINT payments_one_owner;
ALTER TABLE hazem.payments DROP COLUMN service_order_id;
ALTER TABLE hazem.payments ALTER COLUMN appointment_id SET NOT NULL;
DROP TABLE IF EXISTS hazem.service_files;
DROP TABLE IF EXISTS hazem.service_orders;

-- 0014's functions, as they were.
CREATE FUNCTION hazem.receipt_target(p_person uuid)
RETURNS uuid LANGUAGE sql STABLE SET search_path = hazem, pg_temp AS $$
  SELECT a.id FROM hazem.appointments a LEFT JOIN hazem.payments p ON p.appointment_id = a.id
   WHERE a.person_id = p_person
     AND (a.state = 'pending_payment'
          OR a.state = 'confirmed' AND a.ends_at > now() AND p.state IN ('partial', 'not_received', 'requested')
          OR a.state = 'expired' AND a.starts_at > now() AND p.state = 'requested')
   ORDER BY CASE a.state WHEN 'pending_payment' THEN 0 WHEN 'confirmed' THEN 1 ELSE 2 END,
            (p.state = 'requested'),
            CASE WHEN a.state = 'confirmed' THEN a.starts_at END,
            a.updated_at DESC
   LIMIT 1
$$;

CREATE OR REPLACE FUNCTION hazem.attach_receipt(
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

  SELECT * INTO v_a FROM hazem.appointments WHERE id = hazem.receipt_target(p_person) FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'none');
  END IF;

  IF v_a.state = 'pending_payment' THEN
    -- Still held (even a moment after its time, before the worker freed it): nobody else has it.
    UPDATE hazem.appointments
       SET state = 'confirmed', calendar_state = 'create', calendar_attempts = 0, calendar_next_at = now(),
           client_notice = CASE WHEN notice_kind = 'hold_reminder' AND client_notice = 'pending' THEN NULL ELSE client_notice END
     WHERE id = v_a.id;
    v_status := 'confirmed';
  ELSE
    IF v_a.state = 'confirmed' THEN
      v_status := 'received';
    ELSE
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

CREATE OR REPLACE FUNCTION hazem.mark_payment(p_appointment uuid, p_state text, p_received int, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.payments;
  v_over boolean;
  v_tell boolean;
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
  v_tell := p_state IN ('partial', 'not_received')
            AND EXISTS (SELECT 1 FROM hazem.appointments WHERE id = p_appointment AND state = 'confirmed' AND ends_at > now());
  UPDATE hazem.payments
     SET state = CASE WHEN v_over THEN 'refund_requested' ELSE p_state END,
         amount_received = CASE WHEN p_state = 'not_received' THEN NULL ELSE p_received END,
         refund_due = CASE WHEN v_over THEN p_received - v.amount_due END,
         refund_reason = CASE WHEN v_over THEN 'overpaid' END,
         reviewed_by = p_by, reviewed_at = now(),
         client_notice = CASE WHEN v_tell THEN 'pending' ELSE client_notice END,
         notice_kind = CASE WHEN v_tell THEN 'payment_' || p_state ELSE notice_kind END
   WHERE id = v.id;
  UPDATE hazem.appointments SET calendar_state = 'update', calendar_attempts = 0, calendar_next_at = now()
   WHERE id = p_appointment AND state = 'confirmed' AND calendar_state = 'synced';
  RETURN jsonb_build_object('status', 'marked', 'refund', v_over);
END $$;

GRANT EXECUTE ON FUNCTION hazem.receipt_target(uuid) TO office_api;
REVOKE ALL ON FUNCTION hazem.receipt_target(uuid) FROM PUBLIC;
DROP FUNCTION IF EXISTS hazem.file_target(uuid, boolean);

CREATE OR REPLACE FUNCTION hazem.mark_refunded(p_appointment uuid, p_by uuid, p_method text, p_note text)
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
