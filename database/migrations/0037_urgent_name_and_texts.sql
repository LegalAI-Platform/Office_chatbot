-- 0037_urgent_name_and_texts: DECISIONS v1.35 (the owner, 2026-10-31).
--   B-09: the urgent path asks for the name first when it is not known (as every booking, B-02): code only, no new text.
--   Two texts reworded by the owner: «لو سمحت» instead of «معلش» in the second name question (every path), and the urgent
--   waiting text without «ثواني» («ثواني ده وعد مش هيتحقق»).
--   A fix: hazem.forget_wa_id (0025) refused a number whose urgent call had been alerted, since 0035's
--   appointments.urgent_alert_receipt points at the receipt it deletes; it now lets that link go first.

-- migrate:up
UPDATE hazem.approved_texts SET active = false
 WHERE (key = 'ask_name_again' AND active AND text = 'معلش، ممكن اسم حضرتك؟')
    OR (key = 'urgent_waiting' AND active AND text = 'بنوصّل للمستشار، ثواني وهيكلم حضرتك.');
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
SELECT k, (SELECT coalesce(max(version), 0) + 1 FROM hazem.approved_texts WHERE key = k), t, 300,
       NOT EXISTS (SELECT 1 FROM hazem.approved_texts WHERE key = k AND active), s, 'owner approval 2026-10-31'
  FROM (VALUES
    ('ask_name_again', 'لو سمحت، ممكن اسم حضرتك؟', 'DECISIONS v1.35 B-02'),
    ('urgent_waiting', 'بنتواصل مع المستشار دلوقتي، وهيكلم حضرتك.', 'DECISIONS v1.35 B-09')
  ) AS v(k, t, s)
 WHERE NOT EXISTS (SELECT 1 FROM hazem.approved_texts WHERE key = v.k AND text = v.t);

CREATE OR REPLACE FUNCTION hazem.forget_wa_id(p_wa_id text)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path = hazem, app, pg_temp AS $$
DECLARE
  v_people  uuid[];
  v_appts   uuid[];
  v_orders  uuid[];
  v_urf     uuid[];
  v_pays    uuid[];
  v_turns   text[];
  v_events  jsonb;
  v_counts  jsonb := '{}'::jsonb;
  v_n       int;
BEGIN
  IF p_wa_id IS NULL OR p_wa_id !~ '^[0-9]{8,15}$' THEN
    RAISE EXCEPTION 'forget_wa_id: digits only, e.g. 201012345678';
  END IF;

  SELECT coalesce(array_agg(id), '{}') INTO v_people FROM hazem.persons WHERE wa_id = p_wa_id;
  SELECT coalesce(array_agg(id), '{}') INTO v_appts FROM hazem.appointments WHERE person_id = ANY (v_people);
  SELECT coalesce(array_agg(id), '{}') INTO v_orders FROM hazem.service_orders
   WHERE person_id = ANY (v_people) OR appointment_id = ANY (v_appts);
  SELECT coalesce(array_agg(id), '{}') INTO v_urf FROM hazem.urf_cases
   WHERE person_id = ANY (v_people) OR appointment_id = ANY (v_appts);
  SELECT coalesce(array_agg(id), '{}') INTO v_pays FROM hazem.payments
   WHERE appointment_id = ANY (v_appts) OR service_order_id = ANY (v_orders);
  SELECT coalesce(array_agg(turn_id), '{}') INTO v_turns FROM hazem.turns WHERE wa_id = p_wa_id;
  SELECT coalesce(jsonb_agg(jsonb_build_object('event_id', calendar_event_id, 'starts_at', starts_at) ORDER BY starts_at), '[]')
    INTO v_events FROM hazem.appointments WHERE id = ANY (v_appts) AND calendar_event_id IS NOT NULL;

  -- 0037: an urgent call alerted on one of these receipts (0035) points at it: let go first, or the delete is refused.
  UPDATE hazem.appointments SET urgent_alert_receipt = NULL WHERE id = ANY (v_appts) AND urgent_alert_receipt IS NOT NULL;
  DELETE FROM hazem.payment_receipts WHERE payment_id = ANY (v_pays) OR duplicate_of = ANY (v_pays);
  DELETE FROM hazem.payments WHERE id = ANY (v_pays);
  DELETE FROM hazem.service_files WHERE order_id = ANY (v_orders);
  DELETE FROM hazem.urf_files WHERE case_id = ANY (v_urf);
  DELETE FROM hazem.urf_cases WHERE id = ANY (v_urf);
  DELETE FROM hazem.service_orders WHERE id = ANY (v_orders);
  DELETE FROM hazem.appointments WHERE id = ANY (v_appts);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('appointments', v_n);
  DELETE FROM hazem.staff_tasks WHERE person_id = ANY (v_people);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('staff_tasks', v_n);
  DELETE FROM hazem.intakes WHERE person_id = ANY (v_people);
  DELETE FROM hazem.conversations WHERE person_id = ANY (v_people);
  DELETE FROM hazem.persons WHERE id = ANY (v_people);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('persons', v_n);
  DELETE FROM hazem.turn_replies WHERE turn_id = ANY (v_turns);
  DELETE FROM hazem.chatwoot_turns WHERE turn_id = ANY (v_turns);
  DELETE FROM hazem.turns WHERE turn_id = ANY (v_turns);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('turns', v_n);
  DELETE FROM hazem.chatwoot_replies WHERE wa_id = p_wa_id;
  DELETE FROM hazem.chatwoot_threads WHERE wa_id = p_wa_id;

  CREATE TEMP TABLE forget_inbound ON COMMIT DROP AS
    SELECT id, webhook_event_id FROM app.wa_inbound_messages WHERE wa_id = p_wa_id;
  DELETE FROM app.wa_inbound_messages WHERE id IN (SELECT id FROM forget_inbound);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('messages_in', v_n);
  DELETE FROM app.wa_turns WHERE wa_id = p_wa_id;
  DELETE FROM app.wa_message_statuses
   WHERE recipient_id = p_wa_id OR wamid IN (SELECT wamid FROM app.wa_outbound_messages WHERE wa_id = p_wa_id);
  DELETE FROM app.wa_outbound_messages WHERE wa_id = p_wa_id;
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('messages_out', v_n);
  DELETE FROM app.wa_contact_windows WHERE wa_id = p_wa_id;
  DELETE FROM app.waha_menus WHERE wa_id = p_wa_id;
  DELETE FROM app.wa_webhook_events e
   WHERE (e.id IN (SELECT webhook_event_id FROM forget_inbound) OR e.payload::text LIKE '%' || p_wa_id || '%')
     AND NOT EXISTS (SELECT 1 FROM app.wa_inbound_messages i WHERE i.webhook_event_id = e.id);
  DROP TABLE forget_inbound;

  RETURN jsonb_build_object('deleted', v_counts, 'calendar_events', v_events);
END;
$$;

-- migrate:down
CREATE OR REPLACE FUNCTION hazem.forget_wa_id(p_wa_id text)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path = hazem, app, pg_temp AS $$
DECLARE
  v_people  uuid[];
  v_appts   uuid[];
  v_orders  uuid[];
  v_urf     uuid[];
  v_pays    uuid[];
  v_turns   text[];
  v_events  jsonb;
  v_counts  jsonb := '{}'::jsonb;
  v_n       int;
BEGIN
  IF p_wa_id IS NULL OR p_wa_id !~ '^[0-9]{8,15}$' THEN
    RAISE EXCEPTION 'forget_wa_id: digits only, e.g. 201012345678';
  END IF;

  SELECT coalesce(array_agg(id), '{}') INTO v_people FROM hazem.persons WHERE wa_id = p_wa_id;
  SELECT coalesce(array_agg(id), '{}') INTO v_appts FROM hazem.appointments WHERE person_id = ANY (v_people);
  SELECT coalesce(array_agg(id), '{}') INTO v_orders FROM hazem.service_orders
   WHERE person_id = ANY (v_people) OR appointment_id = ANY (v_appts);
  SELECT coalesce(array_agg(id), '{}') INTO v_urf FROM hazem.urf_cases
   WHERE person_id = ANY (v_people) OR appointment_id = ANY (v_appts);
  SELECT coalesce(array_agg(id), '{}') INTO v_pays FROM hazem.payments
   WHERE appointment_id = ANY (v_appts) OR service_order_id = ANY (v_orders);
  SELECT coalesce(array_agg(turn_id), '{}') INTO v_turns FROM hazem.turns WHERE wa_id = p_wa_id;
  SELECT coalesce(jsonb_agg(jsonb_build_object('event_id', calendar_event_id, 'starts_at', starts_at) ORDER BY starts_at), '[]')
    INTO v_events FROM hazem.appointments WHERE id = ANY (v_appts) AND calendar_event_id IS NOT NULL;

  DELETE FROM hazem.payment_receipts WHERE payment_id = ANY (v_pays) OR duplicate_of = ANY (v_pays);
  DELETE FROM hazem.payments WHERE id = ANY (v_pays);
  DELETE FROM hazem.service_files WHERE order_id = ANY (v_orders);
  DELETE FROM hazem.urf_files WHERE case_id = ANY (v_urf);
  DELETE FROM hazem.urf_cases WHERE id = ANY (v_urf);
  DELETE FROM hazem.service_orders WHERE id = ANY (v_orders);
  DELETE FROM hazem.appointments WHERE id = ANY (v_appts);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('appointments', v_n);
  DELETE FROM hazem.staff_tasks WHERE person_id = ANY (v_people);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('staff_tasks', v_n);
  DELETE FROM hazem.intakes WHERE person_id = ANY (v_people);
  DELETE FROM hazem.conversations WHERE person_id = ANY (v_people);
  DELETE FROM hazem.persons WHERE id = ANY (v_people);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('persons', v_n);
  DELETE FROM hazem.turn_replies WHERE turn_id = ANY (v_turns);
  DELETE FROM hazem.chatwoot_turns WHERE turn_id = ANY (v_turns);
  DELETE FROM hazem.turns WHERE turn_id = ANY (v_turns);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('turns', v_n);
  DELETE FROM hazem.chatwoot_replies WHERE wa_id = p_wa_id;
  DELETE FROM hazem.chatwoot_threads WHERE wa_id = p_wa_id;

  CREATE TEMP TABLE forget_inbound ON COMMIT DROP AS
    SELECT id, webhook_event_id FROM app.wa_inbound_messages WHERE wa_id = p_wa_id;
  DELETE FROM app.wa_inbound_messages WHERE id IN (SELECT id FROM forget_inbound);
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('messages_in', v_n);
  DELETE FROM app.wa_turns WHERE wa_id = p_wa_id;
  DELETE FROM app.wa_message_statuses
   WHERE recipient_id = p_wa_id OR wamid IN (SELECT wamid FROM app.wa_outbound_messages WHERE wa_id = p_wa_id);
  DELETE FROM app.wa_outbound_messages WHERE wa_id = p_wa_id;
  GET DIAGNOSTICS v_n = ROW_COUNT; v_counts := v_counts || jsonb_build_object('messages_out', v_n);
  DELETE FROM app.wa_contact_windows WHERE wa_id = p_wa_id;
  DELETE FROM app.waha_menus WHERE wa_id = p_wa_id;
  DELETE FROM app.wa_webhook_events e
   WHERE (e.id IN (SELECT webhook_event_id FROM forget_inbound) OR e.payload::text LIKE '%' || p_wa_id || '%')
     AND NOT EXISTS (SELECT 1 FROM app.wa_inbound_messages i WHERE i.webhook_event_id = e.id);
  DROP TABLE forget_inbound;

  RETURN jsonb_build_object('deleted', v_counts, 'calendar_events', v_events);
END;
$$;
UPDATE hazem.approved_texts SET active = false WHERE source IN ('DECISIONS v1.35 B-02', 'DECISIONS v1.35 B-09');
DELETE FROM hazem.approved_texts t WHERE t.source IN ('DECISIONS v1.35 B-02', 'DECISIONS v1.35 B-09')
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts a SET active = true
 WHERE ((a.key = 'ask_name_again' AND a.text = 'معلش، ممكن اسم حضرتك؟') OR (a.key = 'urgent_waiting' AND a.text = 'بنوصّل للمستشار، ثواني وهيكلم حضرتك.'))
   AND NOT EXISTS (SELECT 1 FROM hazem.approved_texts b WHERE b.key = a.key AND b.active);
