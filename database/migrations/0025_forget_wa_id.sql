-- 0025_forget_wa_id: the owner's «انسى الرقم ده» for testing from the start (asked on 2026-10-28, after the agent had done
-- it by hand several times). hazem.forget_wa_id(wa_id) deletes, in one transaction, everything Hazem and the WhatsApp
-- layer hold for one number: the person, the conversation, the intakes, the staff tasks, the turns and their replies,
-- the appointments with their payments and receipts, the service orders, the Urfi cases and their files, the Chatwoot
-- links, the messages in and out with their statuses, the 24-hour window and the raw webhook events. It returns what
-- it deleted and the appointments' Google Calendar events (Office.Cli forget-number deletes those at Google).
-- The number stays in whatsapp.test_recipients / hazem.recipients. Test environment only: the CLI refuses anywhere else
-- (ENVIRONMENT=test), and the API's role never gets EXECUTE. Chatwoot's copy is its own database (the forget-number
-- script removes it with Chatwoot itself).

-- migrate:up
CREATE FUNCTION hazem.forget_wa_id(p_wa_id text)
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
REVOKE ALL ON FUNCTION hazem.forget_wa_id(text) FROM PUBLIC;
-- The CLI runs as office_api; the API itself never calls it (the CLI checks ENVIRONMENT=test first).
GRANT EXECUTE ON FUNCTION hazem.forget_wa_id(text) TO office_api;

-- migrate:down
DROP FUNCTION IF EXISTS hazem.forget_wa_id(text);
