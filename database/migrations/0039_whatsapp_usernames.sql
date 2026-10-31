-- 0039_whatsapp_usernames: TASK 15, DECISIONS v1.37 (the owner, 2026-10-31, «Plan it», then «تمام نفذ»).
--   A client whose WhatsApp account has a username comes without his phone number: Meta sends «from_user_id» (a BSUID,
--   «EG.» + up to 128 letters and digits) and no «from». wa_process_event put «from» into wa_id (NOT NULL), so the whole
--   event failed and the message was lost (12 from the test phone ending in 90 since 2026-10-28, no alert).
--   Now the contact key (wa_id everywhere) is the phone number, else the BSUID, and a user keeps the key he first had:
--   app.wa_contact_keys maps each BSUID to it (with his number and username when Meta shows them), so one person is
--   never two clients. Sends to a BSUID use Meta's «recipient» field (n8n). Test numbers may also be «@username».

-- migrate:up
CREATE FUNCTION app.wa_is_contact_key(p_key text) RETURNS boolean
LANGUAGE sql IMMUTABLE AS $$ SELECT p_key ~ '^([0-9]{8,15}|[A-Z]{2}\.[A-Za-z0-9.]{1,140})$' $$;

ALTER TABLE app.wa_inbound_messages ADD COLUMN user_id text, ADD COLUMN username text;

CREATE TABLE app.wa_contact_keys (
  user_id     text PRIMARY KEY,
  contact_key text NOT NULL CHECK (app.wa_is_contact_key(contact_key)),
  phone       text CHECK (phone ~ '^[0-9]{8,15}$'),
  username    text CHECK (char_length(username) <= 64),
  updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX wa_contact_keys_key_idx ON app.wa_contact_keys (contact_key);

-- A test recipient (0006): its key, its number, or «@username» in whatsapp.test_recipients.
CREATE FUNCTION app.wa_is_test_recipient(p_key text) RETURNS boolean
LANGUAGE sql STABLE SET search_path = app, public AS $$
  WITH list AS (
    SELECT lower(btrim(x)) AS v FROM unnest(string_to_array(
      COALESCE((SELECT value FROM settings WHERE key = 'whatsapp.test_recipients'), ''), ',')) AS x)
  SELECT EXISTS (SELECT 1 FROM list WHERE v = lower(p_key))
      OR EXISTS (SELECT 1 FROM wa_contact_keys k JOIN list ON list.v IN (k.phone, '@' || lower(k.username))
                  WHERE k.contact_key = p_key)
$$;

CREATE OR REPLACE FUNCTION app.wa_process_event(p_event jsonb) RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  v_event_id   uuid;
  v_hash       text := encode(digest(p_event::text, 'sha256'), 'hex');
  v_field      text := COALESCE(p_event->>'field', 'messages');
  v_phone_id   text := p_event->'metadata'->>'phone_number_id';
  m            jsonb;
  s            jsonb;
  v_wa_id      text;
  v_phone      text;
  v_uid        text;
  v_contact    jsonb;
  v_type       text;
  v_is_group   boolean;
  v_sent_at    timestamptz;
  v_msg_id     uuid;
  v_inserted   int;
  v_msg_new    int := 0;
  v_msg_dup    int := 0;
  v_msg_group  int := 0;
  v_media_new  int := 0;
  v_st_new     int := 0;
  v_tpl        int := 0;
BEGIN
  INSERT INTO wa_webhook_events (field, phone_number_id, payload, payload_sha256)
  VALUES (v_field, v_phone_id, p_event, v_hash)
  ON CONFLICT (payload_sha256) DO NOTHING
  RETURNING id INTO v_event_id;

  IF v_event_id IS NULL THEN
    RETURN jsonb_build_object('duplicate_event', true, 'payload_sha256', v_hash);
  END IF;

  -- Inbound messages
  FOR m IN SELECT * FROM jsonb_array_elements(COALESCE(p_event->'messages', '[]'::jsonb)) LOOP
    -- 0039 (v1.37): a client with a WhatsApp username comes without his number (no «from»), with a BSUID
    -- («from_user_id»). The contact key is the one this user already has, else his number, else the BSUID.
    v_phone    := m->>'from';
    SELECT c INTO v_contact FROM jsonb_array_elements(COALESCE(p_event->'contacts', '[]'::jsonb)) c
     WHERE (v_phone IS NOT NULL AND c->>'wa_id' = v_phone) OR (m->>'from_user_id' IS NOT NULL AND c->>'user_id' = m->>'from_user_id')
     LIMIT 1;
    v_uid      := COALESCE(m->>'from_user_id', v_contact->>'user_id');
    -- a changed BSUID (system «user_changed_user_id») keeps the key of the previous one
    v_wa_id    := COALESCE((SELECT k.contact_key FROM wa_contact_keys k WHERE k.user_id = v_uid),
                           (SELECT k.contact_key FROM wa_contact_keys k WHERE k.user_id = m->'system'->>'previous_user_id'),
                           v_phone, v_uid);
    IF v_uid IS NOT NULL AND v_wa_id IS NOT NULL THEN
      INSERT INTO wa_contact_keys (user_id, contact_key, phone, username)
      VALUES (v_uid, v_wa_id, v_phone, v_contact->'profile'->>'username')
      ON CONFLICT (user_id) DO UPDATE
        SET phone = COALESCE(EXCLUDED.phone, wa_contact_keys.phone),
            username = COALESCE(EXCLUDED.username, wa_contact_keys.username), updated_at = now();
    END IF;
    v_type     := COALESCE(m->>'type', 'unknown');
    v_is_group := (m ? 'group_id') OR COALESCE(m->'context' ? 'group_id', false);
    v_sent_at  := to_timestamp((m->>'timestamp')::bigint);
    v_msg_id   := NULL;

    INSERT INTO wa_inbound_messages
      (wamid, wa_id, contact_name, phone_number_id, message_type, sent_at,
       context_wamid, referral, is_group, raw, webhook_event_id, processing_status, user_id, username)
    VALUES
      (m->>'id', COALESCE(v_wa_id, 'unknown'), v_contact->'profile'->>'name',
       COALESCE(v_phone_id, ''), v_type, v_sent_at,
       m->'context'->>'id', m->'referral', v_is_group, m, v_event_id,
       CASE WHEN v_wa_id IS NULL THEN 'ignored_unsupported' WHEN v_is_group THEN 'ignored_group' ELSE 'pending' END,
       v_uid, v_contact->'profile'->>'username')
    ON CONFLICT (wamid) DO NOTHING
    RETURNING id INTO v_msg_id;

    IF v_msg_id IS NULL THEN
      v_msg_dup := v_msg_dup + 1;
      CONTINUE;
    END IF;
    IF v_is_group OR v_wa_id IS NULL THEN
      v_msg_group := v_msg_group + 1;
      CONTINUE;
    END IF;
    v_msg_new := v_msg_new + 1;

    -- TASK 03: media is downloaded right away by WA-MEDIA-FETCH (Meta's URL expires in minutes).
    IF wa_is_media_type(v_type) THEN
      INSERT INTO wa_media (inbound_message_id, meta_media_id, kind, voice, mime_type, sha256, filename)
      VALUES (v_msg_id, m->v_type->>'id', v_type,
              v_type = 'audio' AND COALESCE((m->'audio'->>'voice')::boolean, false),
              m->v_type->>'mime_type', m->v_type->>'sha256',
              CASE WHEN v_type = 'document' THEN m->'document'->>'filename' END);
      v_media_new := v_media_new + 1;
    END IF;

    INSERT INTO wa_contact_windows (wa_id, last_inbound_at, last_inbound_wamid)
    VALUES (v_wa_id, v_sent_at, m->>'id')
    ON CONFLICT (wa_id) DO UPDATE
      SET last_inbound_at    = GREATEST(wa_contact_windows.last_inbound_at, EXCLUDED.last_inbound_at),
          last_inbound_wamid = CASE WHEN EXCLUDED.last_inbound_at >= wa_contact_windows.last_inbound_at
                                    THEN EXCLUDED.last_inbound_wamid
                                    ELSE wa_contact_windows.last_inbound_wamid END;
  END LOOP;

  -- Delivery statuses for our outbound messages
  FOR s IN SELECT * FROM jsonb_array_elements(COALESCE(p_event->'statuses', '[]'::jsonb)) LOOP
    INSERT INTO wa_message_statuses (wamid, status, status_at, recipient_id, errors, pricing, raw)
    VALUES (s->>'id', s->>'status', to_timestamp((s->>'timestamp')::bigint),
            COALESCE(s->>'recipient_id', s->>'recipient_user_id'), s->'errors', s->'pricing', s)
    ON CONFLICT (wamid, status, status_at) DO NOTHING;
    GET DIAGNOSTICS v_inserted = ROW_COUNT;
    v_st_new := v_st_new + v_inserted;

    UPDATE wa_outbound_messages o
       SET status     = s->>'status',
           last_error = CASE WHEN s->>'status' = 'failed' THEN s->'errors' ELSE o.last_error END
     WHERE o.wamid = s->>'id'
       AND o.status <> 'failed'
       AND wa_status_rank(s->>'status') > wa_status_rank(o.status);
  END LOOP;

  -- Template review results
  IF v_field = 'message_template_status_update' THEN
    INSERT INTO wa_templates (name, language, status, meta_template_id, rejection_reason, last_synced_at)
    VALUES (p_event->>'message_template_name', p_event->>'message_template_language',
            p_event->>'event', p_event->>'message_template_id',
            NULLIF(p_event->>'reason', 'NONE'), now())
    ON CONFLICT (name, language) DO UPDATE
      SET status = EXCLUDED.status,
          meta_template_id = COALESCE(EXCLUDED.meta_template_id, wa_templates.meta_template_id),
          rejection_reason = EXCLUDED.rejection_reason,
          last_synced_at = now();
    v_tpl := 1;
  END IF;

  UPDATE wa_webhook_events
     SET processing_status = CASE WHEN v_msg_new + v_msg_dup + v_msg_group + v_st_new + v_tpl > 0
                                  OR jsonb_array_length(COALESCE(p_event->'statuses', '[]'::jsonb)) > 0
                                  THEN 'processed' ELSE 'ignored' END,
         processed_at = now()
   WHERE id = v_event_id;

  RETURN jsonb_build_object(
    'duplicate_event', false,
    'event_id', v_event_id,
    'messages_new', v_msg_new,
    'messages_duplicate', v_msg_dup,
    'messages_group_ignored', v_msg_group,
    'media_new', v_media_new,
    'statuses_new', v_st_new,
    'templates_updated', v_tpl);
END;
$$;

CREATE OR REPLACE FUNCTION app.wa_outbound_prepare(
  p_idempotency_key   text,
  p_wa_id             text,
  p_kind              text,
  p_request_payload   jsonb,
  p_requested_by      text,
  p_template_name     text DEFAULT NULL,
  p_template_language text DEFAULT NULL
) RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  v_id       uuid;
  v_existing wa_outbound_messages%ROWTYPE;
  v_window   timestamptz;
BEGIN
  IF COALESCE(p_idempotency_key, '') = '' THEN
    RAISE EXCEPTION 'idempotency_key is required' USING ERRCODE = '22023';
  END IF;
  IF NOT COALESCE(wa_is_contact_key(p_wa_id), false) THEN
    RAISE EXCEPTION 'wa_id must be 8-15 digits or a BSUID' USING ERRCODE = '22023';
  END IF;
  IF p_kind NOT IN ('text', 'template', 'media') THEN
    RAISE EXCEPTION 'invalid kind %', p_kind USING ERRCODE = '22023';
  END IF;
  IF COALESCE(p_requested_by, '') = '' THEN
    RAISE EXCEPTION 'requested_by is required' USING ERRCODE = '22023';
  END IF;

  INSERT INTO wa_outbound_messages
    (idempotency_key, wa_id, kind, template_name, template_language, request_payload, requested_by)
  VALUES
    (p_idempotency_key, p_wa_id, p_kind, p_template_name, p_template_language, p_request_payload, p_requested_by)
  ON CONFLICT (idempotency_key) DO NOTHING
  RETURNING id INTO v_id;

  IF v_id IS NULL THEN
    SELECT * INTO v_existing FROM wa_outbound_messages WHERE idempotency_key = p_idempotency_key;
    RETURN jsonb_build_object('action', 'duplicate', 'outbound_id', v_existing.id,
                              'status', v_existing.status, 'wamid', v_existing.wamid);
  END IF;

  IF p_kind IN ('text', 'media') THEN
    SELECT last_inbound_at INTO v_window FROM wa_contact_windows WHERE wa_id = p_wa_id;
    IF v_window IS NULL OR v_window <= now() - interval '24 hours' THEN
      UPDATE wa_outbound_messages
         SET status = 'blocked_window',
             last_error = jsonb_build_object('code', 'OUTSIDE_24H_WINDOW', 'last_inbound_at', v_window)
       WHERE id = v_id;
      RETURN jsonb_build_object('action', 'blocked_window', 'outbound_id', v_id,
                                'status', 'blocked_window', 'error_code', 'OUTSIDE_24H_WINDOW');
    END IF;
  ELSE
    IF NOT EXISTS (SELECT 1 FROM wa_templates
                    WHERE name = p_template_name
                      AND language = COALESCE(p_template_language, 'ar')
                      AND status = 'APPROVED') THEN
      UPDATE wa_outbound_messages
         SET status = 'failed',
             last_error = jsonb_build_object('code', 'TEMPLATE_NOT_APPROVED')
       WHERE id = v_id;
      RETURN jsonb_build_object('action', 'template_not_approved', 'outbound_id', v_id,
                                'status', 'failed', 'error_code', 'TEMPLATE_NOT_APPROVED');
    END IF;
  END IF;

  -- 0006: only the owner's test numbers while sends are restricted (missing setting = restricted).
  IF COALESCE((SELECT value FROM settings WHERE key = 'whatsapp.send_restricted'), 'true') <> 'false'
     AND NOT wa_is_test_recipient(p_wa_id) THEN
    UPDATE wa_outbound_messages
       SET status = 'failed',
           last_error = jsonb_build_object('code', 'NOT_TEST_RECIPIENT')
     WHERE id = v_id;
    RETURN jsonb_build_object('action', 'blocked_recipient', 'outbound_id', v_id,
                              'status', 'failed', 'error_code', 'NOT_TEST_RECIPIENT');
  END IF;

  UPDATE wa_outbound_messages SET status = 'sending', attempts = attempts + 1 WHERE id = v_id;
  RETURN jsonb_build_object('action', 'send', 'outbound_id', v_id, 'status', 'sending');
END;
$$;

-- The turn as the API gets it: the username too (hazem.recipients may name it, and the office sees it).
CREATE OR REPLACE FUNCTION app.wa_turn_json(p_turn_id text) RETURNS jsonb
LANGUAGE sql STABLE SET search_path = app, public AS $$
  SELECT jsonb_build_object(
    'turn_id', t.turn_id,
    'wa_id', t.wa_id,
    'contact_name', (SELECT m.contact_name FROM wa_inbound_messages m
                      WHERE m.turn_id = t.turn_id AND m.contact_name IS NOT NULL
                      ORDER BY m.received_at DESC LIMIT 1),
    'contact_username', (SELECT m.username FROM wa_inbound_messages m
                          WHERE m.turn_id = t.turn_id AND m.username IS NOT NULL
                          ORDER BY m.received_at DESC LIMIT 1),
    'messages', COALESCE((SELECT jsonb_agg(wa_message_json(m.id) ORDER BY m.sent_at, m.received_at)
                            FROM wa_inbound_messages m WHERE m.turn_id = t.turn_id), '[]'::jsonb))
  FROM wa_turns t WHERE t.turn_id = p_turn_id
$$;

-- B-02 (1.37): a client whose WhatsApp hides his number is asked for it at the booking (the owner's words, 2026-10-31).
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('ask_phone', 1, 'ممكن رقم موبايل حضرتك عشان المكتب يقدر يتواصل معاك؟', 300, true, 'DECISIONS v1.37 B-02', 'owner approval 2026-10-31'),
  ('ask_phone_again', 1, 'لو سمحت، ممكن رقم موبايل حضرتك؟', 300, true, 'DECISIONS v1.37 B-02', 'owner approval 2026-10-31'),
  ('phone_needed_for_call', 1, 'من غير رقم المستشار مش هيقدر يكلم حضرتك. تحب نحجزلك مقابلة في المكتب بدلها؟', 300, true,
   'DECISIONS v1.37 B-02', 'owner approval 2026-10-31')
ON CONFLICT (key, version) DO NOTHING;

-- Hazem's side: a client's key may be a BSUID.
ALTER TABLE hazem.persons DROP CONSTRAINT persons_wa_id_check;
ALTER TABLE hazem.persons ADD CONSTRAINT persons_wa_id_check CHECK (wa_id ~ '^([0-9]{8,15}|[A-Z]{2}\.[A-Za-z0-9.]{1,140})$');
ALTER TABLE hazem.persons ADD COLUMN whatsapp_username text CHECK (char_length(whatsapp_username) <= 64);
ALTER TABLE hazem.turns DROP CONSTRAINT turns_wa_id_check;
ALTER TABLE hazem.turns ADD CONSTRAINT turns_wa_id_check CHECK (wa_id ~ '^([0-9]{8,15}|[A-Z]{2}\.[A-Za-z0-9.]{1,140})$');
ALTER TABLE hazem.chatwoot_threads DROP CONSTRAINT chatwoot_threads_wa_id_check;
ALTER TABLE hazem.chatwoot_threads ADD CONSTRAINT chatwoot_threads_wa_id_check CHECK (wa_id ~ '^([0-9]{8,15}|[A-Z]{2}\.[A-Za-z0-9.]{1,140})$');
ALTER TABLE hazem.chatwoot_replies DROP CONSTRAINT chatwoot_replies_wa_id_check;
ALTER TABLE hazem.chatwoot_replies ADD CONSTRAINT chatwoot_replies_wa_id_check CHECK (wa_id ~ '^([0-9]{8,15}|[A-Z]{2}\.[A-Za-z0-9.]{1,140})$');

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
  IF p_wa_id IS NULL OR p_wa_id !~ '^([0-9]{8,15}|[A-Z]{2}[.][A-Za-z0-9.]{1,140})$' THEN
    RAISE EXCEPTION 'forget_wa_id: digits (e.g. 201012345678) or a BSUID';
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
  DELETE FROM app.wa_contact_keys WHERE contact_key = p_wa_id;
  DELETE FROM app.waha_menus WHERE wa_id = p_wa_id;
  DELETE FROM app.wa_webhook_events e
   WHERE (e.id IN (SELECT webhook_event_id FROM forget_inbound) OR e.payload::text LIKE '%' || p_wa_id || '%')
     AND NOT EXISTS (SELECT 1 FROM app.wa_inbound_messages i WHERE i.webhook_event_id = e.id);
  DROP TABLE forget_inbound;

  RETURN jsonb_build_object('deleted', v_counts, 'calendar_events', v_events);
END;
$$;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.source = 'DECISIONS v1.37 B-02'  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);UPDATE hazem.approved_texts SET active = false WHERE source = 'DECISIONS v1.37 B-02';
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
DELETE FROM hazem.chatwoot_replies WHERE wa_id !~ '^[0-9]{8,15}$';
ALTER TABLE hazem.chatwoot_replies DROP CONSTRAINT chatwoot_replies_wa_id_check;
ALTER TABLE hazem.chatwoot_replies ADD CONSTRAINT chatwoot_replies_wa_id_check CHECK (wa_id ~ '^[0-9]{8,15}$');
DELETE FROM hazem.chatwoot_threads WHERE wa_id !~ '^[0-9]{8,15}$';
ALTER TABLE hazem.chatwoot_threads DROP CONSTRAINT chatwoot_threads_wa_id_check;
ALTER TABLE hazem.chatwoot_threads ADD CONSTRAINT chatwoot_threads_wa_id_check CHECK (wa_id ~ '^[0-9]{8,15}$');
ALTER TABLE hazem.turns DROP CONSTRAINT turns_wa_id_check;
ALTER TABLE hazem.turns ADD CONSTRAINT turns_wa_id_check CHECK (wa_id ~ '^[0-9]{8,15}$') NOT VALID;
ALTER TABLE hazem.persons DROP COLUMN IF EXISTS whatsapp_username;
ALTER TABLE hazem.persons DROP CONSTRAINT persons_wa_id_check;
ALTER TABLE hazem.persons ADD CONSTRAINT persons_wa_id_check CHECK (wa_id ~ '^[0-9]{8,15}$') NOT VALID;

CREATE OR REPLACE FUNCTION app.wa_turn_json(p_turn_id text) RETURNS jsonb
LANGUAGE sql STABLE SET search_path = app, public AS $$
  SELECT jsonb_build_object(
    'turn_id', t.turn_id,
    'wa_id', t.wa_id,
    'contact_name', (SELECT m.contact_name FROM wa_inbound_messages m
                      WHERE m.turn_id = t.turn_id AND m.contact_name IS NOT NULL
                      ORDER BY m.received_at DESC LIMIT 1),
    'messages', COALESCE((SELECT jsonb_agg(wa_message_json(m.id) ORDER BY m.sent_at, m.received_at)
                            FROM wa_inbound_messages m WHERE m.turn_id = t.turn_id), '[]'::jsonb))
  FROM wa_turns t WHERE t.turn_id = p_turn_id
$$;

CREATE OR REPLACE FUNCTION app.wa_outbound_prepare(
  p_idempotency_key   text,
  p_wa_id             text,
  p_kind              text,
  p_request_payload   jsonb,
  p_requested_by      text,
  p_template_name     text DEFAULT NULL,
  p_template_language text DEFAULT NULL
) RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  v_id       uuid;
  v_existing wa_outbound_messages%ROWTYPE;
  v_window   timestamptz;
BEGIN
  IF COALESCE(p_idempotency_key, '') = '' THEN
    RAISE EXCEPTION 'idempotency_key is required' USING ERRCODE = '22023';
  END IF;
  IF p_wa_id IS NULL OR p_wa_id !~ '^[0-9]{8,15}$' THEN
    RAISE EXCEPTION 'wa_id must be 8-15 digits' USING ERRCODE = '22023';
  END IF;
  IF p_kind NOT IN ('text', 'template', 'media') THEN
    RAISE EXCEPTION 'invalid kind %', p_kind USING ERRCODE = '22023';
  END IF;
  IF COALESCE(p_requested_by, '') = '' THEN
    RAISE EXCEPTION 'requested_by is required' USING ERRCODE = '22023';
  END IF;

  INSERT INTO wa_outbound_messages
    (idempotency_key, wa_id, kind, template_name, template_language, request_payload, requested_by)
  VALUES
    (p_idempotency_key, p_wa_id, p_kind, p_template_name, p_template_language, p_request_payload, p_requested_by)
  ON CONFLICT (idempotency_key) DO NOTHING
  RETURNING id INTO v_id;

  IF v_id IS NULL THEN
    SELECT * INTO v_existing FROM wa_outbound_messages WHERE idempotency_key = p_idempotency_key;
    RETURN jsonb_build_object('action', 'duplicate', 'outbound_id', v_existing.id,
                              'status', v_existing.status, 'wamid', v_existing.wamid);
  END IF;

  IF p_kind IN ('text', 'media') THEN
    SELECT last_inbound_at INTO v_window FROM wa_contact_windows WHERE wa_id = p_wa_id;
    IF v_window IS NULL OR v_window <= now() - interval '24 hours' THEN
      UPDATE wa_outbound_messages
         SET status = 'blocked_window',
             last_error = jsonb_build_object('code', 'OUTSIDE_24H_WINDOW', 'last_inbound_at', v_window)
       WHERE id = v_id;
      RETURN jsonb_build_object('action', 'blocked_window', 'outbound_id', v_id,
                                'status', 'blocked_window', 'error_code', 'OUTSIDE_24H_WINDOW');
    END IF;
  ELSE
    IF NOT EXISTS (SELECT 1 FROM wa_templates
                    WHERE name = p_template_name
                      AND language = COALESCE(p_template_language, 'ar')
                      AND status = 'APPROVED') THEN
      UPDATE wa_outbound_messages
         SET status = 'failed',
             last_error = jsonb_build_object('code', 'TEMPLATE_NOT_APPROVED')
       WHERE id = v_id;
      RETURN jsonb_build_object('action', 'template_not_approved', 'outbound_id', v_id,
                                'status', 'failed', 'error_code', 'TEMPLATE_NOT_APPROVED');
    END IF;
  END IF;

  -- 0006: only the owner's test numbers while sends are restricted (missing setting = restricted).
  IF COALESCE((SELECT value FROM settings WHERE key = 'whatsapp.send_restricted'), 'true') <> 'false'
     AND NOT p_wa_id = ANY (SELECT btrim(x) FROM unnest(string_to_array(
           COALESCE((SELECT value FROM settings WHERE key = 'whatsapp.test_recipients'), ''), ',')) AS x) THEN
    UPDATE wa_outbound_messages
       SET status = 'failed',
           last_error = jsonb_build_object('code', 'NOT_TEST_RECIPIENT')
     WHERE id = v_id;
    RETURN jsonb_build_object('action', 'blocked_recipient', 'outbound_id', v_id,
                              'status', 'failed', 'error_code', 'NOT_TEST_RECIPIENT');
  END IF;

  UPDATE wa_outbound_messages SET status = 'sending', attempts = attempts + 1 WHERE id = v_id;
  RETURN jsonb_build_object('action', 'send', 'outbound_id', v_id, 'status', 'sending');
END;
$$;

CREATE OR REPLACE FUNCTION app.wa_process_event(p_event jsonb) RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  v_event_id   uuid;
  v_hash       text := encode(digest(p_event::text, 'sha256'), 'hex');
  v_field      text := COALESCE(p_event->>'field', 'messages');
  v_phone_id   text := p_event->'metadata'->>'phone_number_id';
  m            jsonb;
  s            jsonb;
  v_wa_id      text;
  v_type       text;
  v_is_group   boolean;
  v_sent_at    timestamptz;
  v_msg_id     uuid;
  v_inserted   int;
  v_msg_new    int := 0;
  v_msg_dup    int := 0;
  v_msg_group  int := 0;
  v_media_new  int := 0;
  v_st_new     int := 0;
  v_tpl        int := 0;
BEGIN
  INSERT INTO wa_webhook_events (field, phone_number_id, payload, payload_sha256)
  VALUES (v_field, v_phone_id, p_event, v_hash)
  ON CONFLICT (payload_sha256) DO NOTHING
  RETURNING id INTO v_event_id;

  IF v_event_id IS NULL THEN
    RETURN jsonb_build_object('duplicate_event', true, 'payload_sha256', v_hash);
  END IF;

  -- Inbound messages
  FOR m IN SELECT * FROM jsonb_array_elements(COALESCE(p_event->'messages', '[]'::jsonb)) LOOP
    v_wa_id    := m->>'from';
    v_type     := COALESCE(m->>'type', 'unknown');
    v_is_group := (m ? 'group_id') OR COALESCE(m->'context' ? 'group_id', false);
    v_sent_at  := to_timestamp((m->>'timestamp')::bigint);
    v_msg_id   := NULL;

    INSERT INTO wa_inbound_messages
      (wamid, wa_id, contact_name, phone_number_id, message_type, sent_at,
       context_wamid, referral, is_group, raw, webhook_event_id, processing_status)
    VALUES
      (m->>'id', v_wa_id,
       (SELECT c->'profile'->>'name'
          FROM jsonb_array_elements(COALESCE(p_event->'contacts', '[]'::jsonb)) c
         WHERE c->>'wa_id' = v_wa_id LIMIT 1),
       COALESCE(v_phone_id, ''), v_type, v_sent_at,
       m->'context'->>'id', m->'referral', v_is_group, m, v_event_id,
       CASE WHEN v_is_group THEN 'ignored_group' ELSE 'pending' END)
    ON CONFLICT (wamid) DO NOTHING
    RETURNING id INTO v_msg_id;

    IF v_msg_id IS NULL THEN
      v_msg_dup := v_msg_dup + 1;
      CONTINUE;
    END IF;
    IF v_is_group THEN
      v_msg_group := v_msg_group + 1;
      CONTINUE;
    END IF;
    v_msg_new := v_msg_new + 1;

    -- TASK 03: media is downloaded right away by WA-MEDIA-FETCH (Meta's URL expires in minutes).
    IF wa_is_media_type(v_type) THEN
      INSERT INTO wa_media (inbound_message_id, meta_media_id, kind, voice, mime_type, sha256, filename)
      VALUES (v_msg_id, m->v_type->>'id', v_type,
              v_type = 'audio' AND COALESCE((m->'audio'->>'voice')::boolean, false),
              m->v_type->>'mime_type', m->v_type->>'sha256',
              CASE WHEN v_type = 'document' THEN m->'document'->>'filename' END);
      v_media_new := v_media_new + 1;
    END IF;

    INSERT INTO wa_contact_windows (wa_id, last_inbound_at, last_inbound_wamid)
    VALUES (v_wa_id, v_sent_at, m->>'id')
    ON CONFLICT (wa_id) DO UPDATE
      SET last_inbound_at    = GREATEST(wa_contact_windows.last_inbound_at, EXCLUDED.last_inbound_at),
          last_inbound_wamid = CASE WHEN EXCLUDED.last_inbound_at >= wa_contact_windows.last_inbound_at
                                    THEN EXCLUDED.last_inbound_wamid
                                    ELSE wa_contact_windows.last_inbound_wamid END;
  END LOOP;

  -- Delivery statuses for our outbound messages
  FOR s IN SELECT * FROM jsonb_array_elements(COALESCE(p_event->'statuses', '[]'::jsonb)) LOOP
    INSERT INTO wa_message_statuses (wamid, status, status_at, recipient_id, errors, pricing, raw)
    VALUES (s->>'id', s->>'status', to_timestamp((s->>'timestamp')::bigint),
            s->>'recipient_id', s->'errors', s->'pricing', s)
    ON CONFLICT (wamid, status, status_at) DO NOTHING;
    GET DIAGNOSTICS v_inserted = ROW_COUNT;
    v_st_new := v_st_new + v_inserted;

    UPDATE wa_outbound_messages o
       SET status     = s->>'status',
           last_error = CASE WHEN s->>'status' = 'failed' THEN s->'errors' ELSE o.last_error END
     WHERE o.wamid = s->>'id'
       AND o.status <> 'failed'
       AND wa_status_rank(s->>'status') > wa_status_rank(o.status);
  END LOOP;

  -- Template review results
  IF v_field = 'message_template_status_update' THEN
    INSERT INTO wa_templates (name, language, status, meta_template_id, rejection_reason, last_synced_at)
    VALUES (p_event->>'message_template_name', p_event->>'message_template_language',
            p_event->>'event', p_event->>'message_template_id',
            NULLIF(p_event->>'reason', 'NONE'), now())
    ON CONFLICT (name, language) DO UPDATE
      SET status = EXCLUDED.status,
          meta_template_id = COALESCE(EXCLUDED.meta_template_id, wa_templates.meta_template_id),
          rejection_reason = EXCLUDED.rejection_reason,
          last_synced_at = now();
    v_tpl := 1;
  END IF;

  UPDATE wa_webhook_events
     SET processing_status = CASE WHEN v_msg_new + v_msg_dup + v_msg_group + v_st_new + v_tpl > 0
                                  OR jsonb_array_length(COALESCE(p_event->'statuses', '[]'::jsonb)) > 0
                                  THEN 'processed' ELSE 'ignored' END,
         processed_at = now()
   WHERE id = v_event_id;

  RETURN jsonb_build_object(
    'duplicate_event', false,
    'event_id', v_event_id,
    'messages_new', v_msg_new,
    'messages_duplicate', v_msg_dup,
    'messages_group_ignored', v_msg_group,
    'media_new', v_media_new,
    'statuses_new', v_st_new,
    'templates_updated', v_tpl);
END;
$$;

DROP FUNCTION app.wa_is_test_recipient(text);
DROP TABLE app.wa_contact_keys;
ALTER TABLE app.wa_inbound_messages DROP COLUMN IF EXISTS username, DROP COLUMN IF EXISTS user_id;
DROP FUNCTION app.wa_is_contact_key(text);
