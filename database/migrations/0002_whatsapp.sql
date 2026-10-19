-- 0002_whatsapp: Meta WhatsApp Cloud API plumbing (TASK 02).
-- Tables for raw webhook events, inbound messages, the 24-hour window, outbound
-- messages, delivery statuses, templates, non-secret settings and workflow errors,
-- plus the functions n8n calls so every rule runs inside one database transaction.

-- migrate:up

-- ---------------------------------------------------------------------------
-- Settings (non-secret configuration only)
-- ---------------------------------------------------------------------------
CREATE TABLE app.settings (
  key         text PRIMARY KEY,
  value       text NOT NULL DEFAULT '',
  description text,
  updated_at  timestamptz NOT NULL DEFAULT now(),
  updated_by  text,
  CONSTRAINT settings_no_secrets CHECK (key !~* '(secret|token|password|api_key)')
);
COMMENT ON TABLE app.settings IS
  'Non-secret configuration read by n8n (CFG-GET). Secrets belong in n8n Credentials only.';
CREATE TRIGGER trg_settings_updated_at BEFORE UPDATE ON app.settings
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

INSERT INTO app.settings (key, value, description) VALUES
  ('whatsapp.app_id',             '',      'Meta App ID'),
  ('whatsapp.phone_number_id',    '',      'Phone number ID used to send messages'),
  ('whatsapp.waba_id',            '',      'WhatsApp Business Account ID'),
  ('whatsapp.graph_api_version',  '',      'Graph API version, e.g. v24.0 (checked against the Meta changelog)'),
  ('whatsapp.test_recipients',    '',      'Comma-separated wa_id list allowed to receive test messages'),
  ('whatsapp.echo_test_enabled',  'false', 'true only while running WA-ECHO-TEST'),
  ('ops.telegram_chat_id',        '',      'Telegram chat that receives technical alerts (empty = log only)');

-- ---------------------------------------------------------------------------
-- Raw webhook events (one row per change value emitted by the WhatsApp Trigger)
-- ---------------------------------------------------------------------------
CREATE TABLE app.wa_webhook_events (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  received_at       timestamptz NOT NULL DEFAULT now(),
  field             text,
  phone_number_id   text,
  payload           jsonb NOT NULL,
  payload_sha256    text NOT NULL UNIQUE,
  processing_status text NOT NULL DEFAULT 'received'
    CHECK (processing_status IN ('received', 'processed', 'ignored', 'failed')),
  error             jsonb,
  processed_at      timestamptz
);
CREATE INDEX wa_webhook_events_status_idx ON app.wa_webhook_events (processing_status, received_at);

-- ---------------------------------------------------------------------------
-- Inbound messages (TASK 03 picks up rows in 'pending')
-- ---------------------------------------------------------------------------
CREATE TABLE app.wa_inbound_messages (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  wamid             text NOT NULL UNIQUE,
  wa_id             text NOT NULL,
  contact_name      text,
  phone_number_id   text NOT NULL,
  message_type      text NOT NULL,
  sent_at           timestamptz NOT NULL,
  received_at       timestamptz NOT NULL DEFAULT now(),
  context_wamid     text,
  referral          jsonb,
  is_group          boolean NOT NULL DEFAULT false,
  raw               jsonb NOT NULL,
  webhook_event_id  uuid REFERENCES app.wa_webhook_events (id),
  processing_status text NOT NULL DEFAULT 'pending'
    CHECK (processing_status IN ('pending', 'processing', 'processed',
                                 'ignored_group', 'ignored_unsupported', 'failed')),
  processed_at      timestamptz,
  updated_at        timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX wa_inbound_messages_wa_id_idx  ON app.wa_inbound_messages (wa_id, sent_at);
CREATE INDEX wa_inbound_messages_status_idx ON app.wa_inbound_messages (processing_status, received_at);
CREATE TRIGGER trg_wa_inbound_messages_updated_at BEFORE UPDATE ON app.wa_inbound_messages
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- ---------------------------------------------------------------------------
-- 24-hour customer service window (M-02)
-- ---------------------------------------------------------------------------
CREATE TABLE app.wa_contact_windows (
  wa_id              text PRIMARY KEY,
  last_inbound_at    timestamptz NOT NULL,
  last_inbound_wamid text,
  updated_at         timestamptz NOT NULL DEFAULT now()
);
CREATE TRIGGER trg_wa_contact_windows_updated_at BEFORE UPDATE ON app.wa_contact_windows
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- ---------------------------------------------------------------------------
-- Outbound messages
-- ---------------------------------------------------------------------------
CREATE TABLE app.wa_outbound_messages (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  idempotency_key   text NOT NULL UNIQUE,
  wa_id             text NOT NULL,
  kind              text NOT NULL CHECK (kind IN ('text', 'template', 'media')),
  template_name     text,
  template_language text,
  request_payload   jsonb NOT NULL,
  response_payload  jsonb,
  wamid             text UNIQUE,
  status            text NOT NULL DEFAULT 'queued'
    CHECK (status IN ('queued', 'sending', 'sent', 'delivered', 'read', 'failed', 'blocked_window')),
  attempts          int NOT NULL DEFAULT 0,
  last_error        jsonb,
  requested_by      text NOT NULL,
  created_at        timestamptz NOT NULL DEFAULT now(),
  sent_at           timestamptz,
  updated_at        timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX wa_outbound_messages_wa_id_idx ON app.wa_outbound_messages (wa_id, created_at);
CREATE TRIGGER trg_wa_outbound_messages_updated_at BEFORE UPDATE ON app.wa_outbound_messages
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- ---------------------------------------------------------------------------
-- Delivery statuses
-- ---------------------------------------------------------------------------
CREATE TABLE app.wa_message_statuses (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  wamid        text NOT NULL,
  status       text NOT NULL,
  status_at    timestamptz NOT NULL,
  recipient_id text,
  errors       jsonb,
  pricing      jsonb,
  raw          jsonb NOT NULL,
  received_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (wamid, status, status_at)
);

-- ---------------------------------------------------------------------------
-- Message templates (synced from Meta)
-- ---------------------------------------------------------------------------
CREATE TABLE app.wa_templates (
  name             text NOT NULL,
  language         text NOT NULL,
  category         text,
  status           text,
  meta_template_id text,
  components       jsonb,
  rejection_reason text,
  last_synced_at   timestamptz,
  updated_at       timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (name, language)
);
CREATE TRIGGER trg_wa_templates_updated_at BEFORE UPDATE ON app.wa_templates
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- ---------------------------------------------------------------------------
-- Workflow errors (META-WA-ERROR-HANDLER)
-- ---------------------------------------------------------------------------
CREATE TABLE app.workflow_errors (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  occurred_at   timestamptz NOT NULL DEFAULT now(),
  workflow_name text,
  execution_id  text,
  node_name     text,
  message       text,
  details       jsonb,
  notified      boolean NOT NULL DEFAULT false
);
CREATE INDEX workflow_errors_occurred_idx ON app.workflow_errors (occurred_at);

-- ---------------------------------------------------------------------------
-- Functions
-- ---------------------------------------------------------------------------

-- Ordering of delivery statuses; 'failed' is terminal.
CREATE FUNCTION app.wa_status_rank(p_status text) RETURNS int
LANGUAGE sql IMMUTABLE AS $$
  SELECT CASE p_status
    WHEN 'queued' THEN 0 WHEN 'sending' THEN 1 WHEN 'sent' THEN 2
    WHEN 'delivered' THEN 3 WHEN 'read' THEN 4 WHEN 'failed' THEN 99
    ELSE -1 END
$$;

-- Processes one event as emitted by the n8n WhatsApp Trigger
-- (a change "value" object plus "field"). Idempotent: the same event, or the
-- same wamid inside another event, never creates duplicate rows.
CREATE FUNCTION app.wa_process_event(p_event jsonb) RETURNS jsonb
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
  v_is_group   boolean;
  v_sent_at    timestamptz;
  v_inserted   int;
  v_msg_new    int := 0;
  v_msg_dup    int := 0;
  v_msg_group  int := 0;
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
    v_is_group := (m ? 'group_id') OR COALESCE(m->'context' ? 'group_id', false);
    v_sent_at  := to_timestamp((m->>'timestamp')::bigint);

    INSERT INTO wa_inbound_messages
      (wamid, wa_id, contact_name, phone_number_id, message_type, sent_at,
       context_wamid, referral, is_group, raw, webhook_event_id, processing_status)
    VALUES
      (m->>'id', v_wa_id,
       (SELECT c->'profile'->>'name'
          FROM jsonb_array_elements(COALESCE(p_event->'contacts', '[]'::jsonb)) c
         WHERE c->>'wa_id' = v_wa_id LIMIT 1),
       COALESCE(v_phone_id, ''), COALESCE(m->>'type', 'unknown'), v_sent_at,
       m->'context'->>'id', m->'referral', v_is_group, m, v_event_id,
       CASE WHEN v_is_group THEN 'ignored_group' ELSE 'pending' END)
    ON CONFLICT (wamid) DO NOTHING;
    GET DIAGNOSTICS v_inserted = ROW_COUNT;

    IF v_inserted = 0 THEN
      v_msg_dup := v_msg_dup + 1;
      CONTINUE;
    END IF;
    IF v_is_group THEN
      v_msg_group := v_msg_group + 1;
      CONTINUE;
    END IF;
    v_msg_new := v_msg_new + 1;

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
    'statuses_new', v_st_new,
    'templates_updated', v_tpl);
END;
$$;

-- Decides whether an outbound message may be sent, and records it.
-- action: send | duplicate | blocked_window | template_not_approved
-- Raises an exception (SQLSTATE 22023) on invalid input.
CREATE FUNCTION app.wa_outbound_prepare(
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

  UPDATE wa_outbound_messages SET status = 'sending', attempts = attempts + 1 WHERE id = v_id;
  RETURN jsonb_build_object('action', 'send', 'outbound_id', v_id, 'status', 'sending');
END;
$$;

-- Records the Graph API result for a prepared message and maps Meta errors.
CREATE FUNCTION app.wa_outbound_result(
  p_outbound_id uuid,
  p_ok          boolean,
  p_response    jsonb,
  p_error       jsonb DEFAULT NULL
) RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  v_wamid text := p_response->'messages'->0->>'id';
  v_code  int  := NULLIF(COALESCE(p_error->'error'->>'code', p_error->>'code'), '')::int;
  v_error_code text;
BEGIN
  IF p_ok AND v_wamid IS NOT NULL THEN
    UPDATE wa_outbound_messages
       SET status = CASE WHEN wa_status_rank(status) > wa_status_rank('sent') THEN status ELSE 'sent' END,
           wamid = v_wamid, response_payload = p_response, sent_at = now(), last_error = NULL
     WHERE id = p_outbound_id;
    RETURN jsonb_build_object('ok', true, 'outbound_id', p_outbound_id, 'wamid', v_wamid, 'status', 'sent');
  END IF;

  v_error_code := CASE
    WHEN v_code = 131047                 THEN 'OUTSIDE_24H_WINDOW'
    WHEN v_code = 190                    THEN 'TOKEN_INVALID'
    WHEN v_code IN (130429, 131056, 4, 80007) THEN 'RATE_LIMIT'
    WHEN v_code = 131026                 THEN 'UNDELIVERABLE'
    WHEN v_code = 131030                 THEN 'RECIPIENT_NOT_ALLOWED'
    WHEN v_code IS NULL                  THEN 'NETWORK_OR_UNKNOWN'
    ELSE 'META_ERROR' END;

  UPDATE wa_outbound_messages
     SET status = 'failed', response_payload = p_response,
         last_error = COALESCE(p_error, '{}'::jsonb) || jsonb_build_object('mapped_code', v_error_code)
   WHERE id = p_outbound_id;

  RETURN jsonb_build_object('ok', false, 'outbound_id', p_outbound_id, 'status', 'failed',
                            'error_code', v_error_code, 'meta_code', v_code,
                            'error_message', COALESCE(p_error->'error'->>'message', p_error->>'message'));
END;
$$;

-- Only the application role may call the functions.
REVOKE ALL ON FUNCTION app.wa_process_event(jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_outbound_prepare(text, text, text, jsonb, text, text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_outbound_result(uuid, boolean, jsonb, jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION app.wa_process_event(jsonb) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_outbound_prepare(text, text, text, jsonb, text, text, text) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_outbound_result(uuid, boolean, jsonb, jsonb) TO office_app;

-- migrate:down
DROP FUNCTION IF EXISTS app.wa_outbound_result(uuid, boolean, jsonb, jsonb);
DROP FUNCTION IF EXISTS app.wa_outbound_prepare(text, text, text, jsonb, text, text, text);
DROP FUNCTION IF EXISTS app.wa_process_event(jsonb);
DROP FUNCTION IF EXISTS app.wa_status_rank(text);
DROP TABLE IF EXISTS app.workflow_errors;
DROP TABLE IF EXISTS app.wa_templates;
DROP TABLE IF EXISTS app.wa_message_statuses;
DROP TABLE IF EXISTS app.wa_outbound_messages;
DROP TABLE IF EXISTS app.wa_contact_windows;
DROP TABLE IF EXISTS app.wa_inbound_messages;
DROP TABLE IF EXISTS app.wa_webhook_events;
DROP TABLE IF EXISTS app.settings;
