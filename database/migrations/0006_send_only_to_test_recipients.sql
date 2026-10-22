-- 0006_send_only_to_test_recipients: one guard for EVERY send (CLAUDE.md: never send to anyone
-- outside whatsapp.test_recipients without the owner's explicit approval). Until now the list was
-- only stored. app.wa_outbound_prepare is the single gate n8n uses before any text, media or
-- template goes to Meta, so the check lives there: Hazem, the CLI, and later reminders and the
-- panel are all covered. n8n already treats any action other than "send" as not sent.
--   whatsapp.send_restricted = 'true' (default): only wa_ids in whatsapp.test_recipients
--   whatsapp.send_restricted = 'false': everybody — the owner's decision only (healthcheck WARN)
-- The check runs last, so the existing refusals (24-hour window, template) keep their codes.

-- migrate:up
INSERT INTO app.settings (key, value, description) VALUES
  ('whatsapp.send_restricted', 'true',
   'true = send only to whatsapp.test_recipients (every send, any sender). false = everybody: the owner''s explicit decision only');

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

-- migrate:down
-- The 0002 version, unchanged.
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

  UPDATE wa_outbound_messages SET status = 'sending', attempts = attempts + 1 WHERE id = v_id;
  RETURN jsonb_build_object('action', 'send', 'outbound_id', v_id, 'status', 'sending');
END;
$$;

DELETE FROM app.settings WHERE key = 'whatsapp.send_restricted';
