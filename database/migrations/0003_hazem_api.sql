-- 0003_hazem_api: the .NET backend's schema "hazem" and the hand-off between n8n and the
-- API (DECISIONS v1.3, T-04 and T-18 to T-22).
--   schema app   (wa_*): owned by n8n through office_app; office_api has no access.
--   schema hazem       : owned by the API through office_api; office_app has no access.
-- docs/hazem-internal-api.md describes the API contract.

-- migrate:up

-- ---------------------------------------------------------------------------
-- Schema hazem (the API's data)
-- ---------------------------------------------------------------------------
CREATE SCHEMA hazem AUTHORIZATION office_migrator;
-- No USAGE for office_app: the database itself keeps n8n out of the API's tables.
GRANT USAGE ON SCHEMA hazem TO office_api, office_readonly;

-- Non-secret API settings (same rule as app.settings).
CREATE TABLE hazem.settings (
  key         text PRIMARY KEY,
  value       text NOT NULL DEFAULT '',
  description text,
  updated_at  timestamptz NOT NULL DEFAULT now(),
  updated_by  text,
  CONSTRAINT settings_no_secrets CHECK (key !~* '(secret|token|password|api_key)')
);
CREATE TRIGGER trg_settings_updated_at BEFORE UPDATE ON hazem.settings
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

INSERT INTO hazem.settings (key, value, description) VALUES
  ('echo.enabled',    'false', 'true only while testing: answer text turns from echo.recipients'),
  ('echo.recipients', '',      'Comma-separated wa_id list that gets the echo reply');

-- One row per turn received from n8n (POST /api/hazem/turns). turn_id makes it idempotent.
CREATE TABLE hazem.turns (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  turn_id      text NOT NULL UNIQUE CHECK (length(turn_id) BETWEEN 1 AND 200),
  wa_id        text NOT NULL CHECK (wa_id ~ '^[0-9]{8,15}$'),
  contact_name text,
  messages     jsonb NOT NULL,
  status       text NOT NULL CHECK (status IN ('processed', 'failed')),
  responder    text NOT NULL,
  actions      jsonb NOT NULL DEFAULT '[]'::jsonb,
  created_at   timestamptz NOT NULL DEFAULT now(),
  updated_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX turns_wa_id_idx ON hazem.turns (wa_id, created_at);
CREATE TRIGGER trg_turns_updated_at BEFORE UPDATE ON hazem.turns
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- What happened to every message the API asked n8n to send (one row per idempotency key).
CREATE TABLE hazem.send_results (
  idempotency_key text PRIMARY KEY,
  turn_id         text,
  ok              boolean NOT NULL,
  outbound_id     text,
  wamid           text,
  status          text,
  error_code      text,
  error_message   text,
  reported_by     text NOT NULL CHECK (reported_by IN ('n8n', 'api')),
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX send_results_turn_idx ON hazem.send_results (turn_id);
CREATE TRIGGER trg_send_results_updated_at BEFORE UPDATE ON hazem.send_results
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hazem TO office_api;
GRANT SELECT ON ALL TABLES IN SCHEMA hazem TO office_readonly;
-- The database-wide default privileges also grant office_app; take that back here
-- (office_app has no USAGE on hazem anyway).
REVOKE ALL ON ALL TABLES IN SCHEMA hazem FROM office_app;
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator IN SCHEMA hazem
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO office_api;
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator IN SCHEMA hazem
  GRANT USAGE, SELECT ON SEQUENCES TO office_api;

-- ---------------------------------------------------------------------------
-- WhatsApp side (schema app): settings and the hand-off functions n8n calls
-- ---------------------------------------------------------------------------
-- The echo test moved to the API (hazem.settings echo.*).
DELETE FROM app.settings WHERE key = 'whatsapp.echo_test_enabled';
INSERT INTO app.settings (key, value, description) VALUES
  ('whatsapp.dry_run', 'false',
   'true only while testing: sends are recorded as sent without calling Meta. Must stay false.');

ALTER TABLE app.wa_inbound_messages ADD COLUMN last_error jsonb;

-- Claims messages that are ready for the API and returns them as turns.
-- Until TASK 03 one turn = one text message (turn_id 'wa:<wamid>'); media stays pending.
-- Rows left in 'processing' for more than 5 minutes (n8n stopped half-way) are claimed again.
-- p_only_wa_ids limits the claim to some numbers (tests only).
CREATE FUNCTION app.wa_turns_claim(p_limit int DEFAULT 20, p_only_wa_ids text[] DEFAULT NULL)
RETURNS jsonb
LANGUAGE sql
SET search_path = app, public
AS $$
  WITH picked AS (
    SELECT id
      FROM wa_inbound_messages
     WHERE message_type = 'text'
       AND NOT is_group
       AND (processing_status = 'pending'
            OR (processing_status = 'processing' AND updated_at < now() - interval '5 minutes'))
       AND (p_only_wa_ids IS NULL OR wa_id = ANY (p_only_wa_ids))
     ORDER BY received_at
     LIMIT GREATEST(LEAST(p_limit, 100), 1)
     FOR UPDATE SKIP LOCKED
  ), claimed AS (
    UPDATE wa_inbound_messages m
       SET processing_status = 'processing', last_error = NULL
      FROM picked
     WHERE m.id = picked.id
    RETURNING m.wamid, m.wa_id, m.contact_name, m.message_type, m.sent_at, m.received_at,
              m.context_wamid, m.raw
  )
  SELECT COALESCE(jsonb_agg(jsonb_build_object(
           'turn_id', 'wa:' || wamid,
           'wa_id', wa_id,
           'contact_name', contact_name,
           'messages', jsonb_build_array(jsonb_build_object(
              'wamid', wamid,
              'type', message_type,
              'text', raw->'text'->>'body',
              'sent_at', sent_at,
              'context_wamid', context_wamid)))
           ORDER BY received_at), '[]'::jsonb)
    FROM claimed;
$$;

-- Finishes claimed messages: processed | failed | pending (give back for a later retry).
-- Only rows still in 'processing' change, so a late call cannot undo newer work.
CREATE FUNCTION app.wa_turns_finish(p_wamids text[], p_status text, p_error jsonb DEFAULT NULL)
RETURNS int
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  v_count int;
BEGIN
  IF p_status NOT IN ('processed', 'failed', 'pending') THEN
    RAISE EXCEPTION 'invalid status %', p_status USING ERRCODE = '22023';
  END IF;

  UPDATE wa_inbound_messages
     SET processing_status = p_status,
         processed_at = CASE WHEN p_status = 'pending' THEN NULL ELSE now() END,
         last_error = p_error
   WHERE wamid = ANY (p_wamids)
     AND processing_status = 'processing';
  GET DIAGNOSTICS v_count = ROW_COUNT;
  RETURN v_count;
END;
$$;

REVOKE ALL ON FUNCTION app.wa_turns_claim(int, text[]) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_turns_finish(text[], text, jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION app.wa_turns_claim(int, text[]) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_turns_finish(text[], text, jsonb) TO office_app;

-- migrate:down
DROP FUNCTION IF EXISTS app.wa_turns_finish(text[], text, jsonb);
DROP FUNCTION IF EXISTS app.wa_turns_claim(int, text[]);
ALTER TABLE app.wa_inbound_messages DROP COLUMN IF EXISTS last_error;
DELETE FROM app.settings WHERE key = 'whatsapp.dry_run';
INSERT INTO app.settings (key, value, description) VALUES
  ('whatsapp.echo_test_enabled', 'false', 'true only while running WA-ECHO-TEST')
  ON CONFLICT (key) DO NOTHING;
DROP TABLE IF EXISTS hazem.send_results;
DROP TABLE IF EXISTS hazem.turns;
DROP TABLE IF EXISTS hazem.settings;
DROP SCHEMA IF EXISTS hazem;
