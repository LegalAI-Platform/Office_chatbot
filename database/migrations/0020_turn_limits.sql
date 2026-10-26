-- 0020_turn_limits: the security review of 2026-10-26 (docs/security/review-2026-10-26.md, findings 2 and 3).
--   * A turn the API keeps failing on is tried at most whatsapp.turn_max_attempts times (5), then failed for good:
--     before, it was given back and taken again every minute forever (an OpenAI call each time, and the client's
--     later messages stuck behind it). Each one becomes a system alert for the managers (hazem.failed_turns).
--   * At most whatsapp.turns_per_client_hour new turns (30) per client an hour; past it the client's messages
--     wait and are grouped into the next turn. Nothing is dropped; a flood just stops costing a turn per message.
-- wa_turns_claim is the 0004 function with these two rules; the down migration puts the 0004 one back.

-- migrate:up
INSERT INTO app.settings (key, value, description) VALUES
  ('whatsapp.turn_max_attempts',     '5',  'A turn the API failed on this many times is marked failed (TOO_MANY_ATTEMPTS) and the managers are alerted'),
  ('whatsapp.turns_per_client_hour', '30', 'At most this many turns per client an hour; later messages wait and are grouped into the next turn');

CREATE OR REPLACE FUNCTION app.wa_turns_claim(p_limit int DEFAULT 20, p_only_wa_ids text[] DEFAULT NULL)
RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  v_quiet   interval := make_interval(secs => wa_setting_int('whatsapp.group_quiet_seconds', 8));
  v_max     interval := make_interval(secs => wa_setting_int('whatsapp.group_max_seconds', 60));
  v_stale   interval := make_interval(secs => wa_setting_int('whatsapp.turn_stale_seconds', 300));
  v_tries   int := GREATEST(wa_setting_int('whatsapp.turn_max_attempts', 5), 1);
  v_hourly  int := GREATEST(wa_setting_int('whatsapp.turns_per_client_hour', 30), 1);
  v_limit   int := GREATEST(LEAST(COALESCE(p_limit, 20), 100), 1);
  v_result  jsonb := '[]'::jsonb;
  v_count   int := 0;
  v_turn_id text;
  v_first   timestamptz;
  v_last    timestamptz;
  v_rows    int;
  t         record;
  c         record;
BEGIN
  -- a) Retry open turns.
  FOR t IN
    SELECT turn_id, wa_id FROM wa_turns
     WHERE (status = 'pending' OR (status = 'processing' AND claimed_at < now() - v_stale))
       AND (p_only_wa_ids IS NULL OR wa_id = ANY (p_only_wa_ids))
     ORDER BY created_at
  LOOP
    EXIT WHEN v_count >= v_limit;
    CONTINUE WHEN NOT pg_try_advisory_xact_lock(hashtext('wa-turn:' || t.wa_id));
    -- Tried turn_max_attempts times: failed for good (the managers get a system alert), so one message the API
    -- cannot handle never loops forever, nor keeps the client's later messages waiting behind it.
    UPDATE wa_turns
       SET status = 'failed',
           last_error = jsonb_build_object('code', 'TOO_MANY_ATTEMPTS', 'attempts', attempts, 'last', last_error)
     WHERE turn_id = t.turn_id AND attempts >= v_tries
       AND (status = 'pending' OR (status = 'processing' AND claimed_at < now() - v_stale));
    IF FOUND THEN
      UPDATE wa_inbound_messages
         SET processing_status = 'failed', processed_at = now(),
             last_error = jsonb_build_object('code', 'TOO_MANY_ATTEMPTS')
       WHERE turn_id = t.turn_id;
      CONTINUE;
    END IF;
    UPDATE wa_turns
       SET status = 'processing', claimed_at = now(), attempts = attempts + 1
     WHERE turn_id = t.turn_id
       AND (status = 'pending' OR (status = 'processing' AND claimed_at < now() - v_stale));
    CONTINUE WHEN NOT FOUND;
    UPDATE wa_inbound_messages SET processing_status = 'processing' WHERE turn_id = t.turn_id;
    v_result := v_result || jsonb_build_array(wa_turn_json(t.turn_id));
    v_count := v_count + 1;
  END LOOP;

  -- b) New turns.
  FOR c IN
    SELECT m.wa_id
      FROM wa_inbound_messages m
     WHERE m.processing_status = 'pending' AND m.turn_id IS NULL AND NOT m.is_group
       AND (p_only_wa_ids IS NULL OR m.wa_id = ANY (p_only_wa_ids))
     GROUP BY m.wa_id
    HAVING max(m.received_at) <= now() - v_quiet OR min(m.received_at) <= now() - v_max
     ORDER BY min(m.received_at)
  LOOP
    EXIT WHEN v_count >= v_limit;
    CONTINUE WHEN NOT pg_try_advisory_xact_lock(hashtext('wa-turn:' || c.wa_id));
    CONTINUE WHEN EXISTS (SELECT 1 FROM wa_turns
                           WHERE wa_id = c.wa_id AND status IN ('processing', 'pending'));
    -- At most turns_per_client_hour turns a client an hour: past it the messages wait (nothing is lost) and go
    -- together into the next turn, so a flood costs one classification, not hundreds.
    CONTINUE WHEN (SELECT count(*) FROM wa_turns
                    WHERE wa_id = c.wa_id AND created_at > now() - interval '1 hour') >= v_hourly;

    -- Re-read under the lock.
    SELECT min(received_at), max(received_at) INTO v_first, v_last
      FROM wa_inbound_messages
     WHERE wa_id = c.wa_id AND processing_status = 'pending' AND turn_id IS NULL AND NOT is_group;
    CONTINUE WHEN v_first IS NULL;
    IF v_first > now() - v_max THEN
      CONTINUE WHEN v_last > now() - v_quiet;
      CONTINUE WHEN EXISTS (SELECT 1 FROM wa_inbound_messages m
                             WHERE m.wa_id = c.wa_id AND m.processing_status = 'pending'
                               AND m.turn_id IS NULL AND NOT m.is_group AND NOT wa_message_ready(m.id));
    ELSE
      -- Max wait reached: unfinished media and transcripts become failed (timeout) now, in the
      -- same transaction, so the database says exactly what the API is told.
      UPDATE wa_media w
         SET status = 'failed', last_error = jsonb_build_object('code', 'TIMEOUT', 'stage', 'download')
        FROM wa_inbound_messages m
       WHERE w.inbound_message_id = m.id AND m.wa_id = c.wa_id AND m.processing_status = 'pending'
         AND m.turn_id IS NULL AND w.status IN ('pending', 'downloading');
      UPDATE wa_media w
         SET transcript_status = 'failed',
             last_error = jsonb_build_object('code', 'TIMEOUT', 'stage', 'transcription')
        FROM wa_inbound_messages m
       WHERE w.inbound_message_id = m.id AND m.wa_id = c.wa_id AND m.processing_status = 'pending'
         AND m.turn_id IS NULL AND w.transcript_status = 'pending';
    END IF;

    v_turn_id := 'wt:' || gen_random_uuid();
    INSERT INTO wa_turns (turn_id, wa_id, status, claimed_at, attempts)
    VALUES (v_turn_id, c.wa_id, 'processing', now(), 1);
    UPDATE wa_inbound_messages
       SET processing_status = 'processing', turn_id = v_turn_id, last_error = NULL
     WHERE wa_id = c.wa_id AND processing_status = 'pending' AND turn_id IS NULL AND NOT is_group;
    GET DIAGNOSTICS v_rows = ROW_COUNT;
    IF v_rows = 0 THEN
      DELETE FROM wa_turns WHERE turn_id = v_turn_id;
      CONTINUE;
    END IF;
    v_result := v_result || jsonb_build_array(wa_turn_json(v_turn_id));
    v_count := v_count + 1;
  END LOOP;

  RETURN v_result;
END;
$$;

-- Turns failed for good since p_since, for the API's system alerts: only the turn id and when (never the number
-- or the text). SECURITY DEFINER because the API has no access to schema app.
CREATE FUNCTION hazem.failed_turns(p_since timestamptz)
RETURNS TABLE (turn_id text, failed_at timestamptz)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = app, pg_temp AS $$
  SELECT t.turn_id, t.updated_at FROM app.wa_turns t
   WHERE t.status = 'failed' AND t.last_error ->> 'code' = 'TOO_MANY_ATTEMPTS' AND t.updated_at >= p_since
$$;
REVOKE ALL ON FUNCTION hazem.failed_turns(timestamptz) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION hazem.failed_turns(timestamptz) TO office_api;

ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_system_what_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_system_what_check
  CHECK (system_what IN ('backup', 'healthcheck', 'drill', 'log_scan', 'n8n', 'turn'));

-- migrate:down
DELETE FROM hazem.panel_alerts WHERE system_what = 'turn';
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_system_what_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_system_what_check
  CHECK (system_what IN ('backup', 'healthcheck', 'drill', 'log_scan', 'n8n'));
DROP FUNCTION IF EXISTS hazem.failed_turns(timestamptz);
DELETE FROM app.settings WHERE key IN ('whatsapp.turn_max_attempts', 'whatsapp.turns_per_client_hour');

CREATE OR REPLACE FUNCTION app.wa_turns_claim(p_limit int DEFAULT 20, p_only_wa_ids text[] DEFAULT NULL)
RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  v_quiet   interval := make_interval(secs => wa_setting_int('whatsapp.group_quiet_seconds', 8));
  v_max     interval := make_interval(secs => wa_setting_int('whatsapp.group_max_seconds', 60));
  v_stale   interval := make_interval(secs => wa_setting_int('whatsapp.turn_stale_seconds', 300));
  v_limit   int := GREATEST(LEAST(COALESCE(p_limit, 20), 100), 1);
  v_result  jsonb := '[]'::jsonb;
  v_count   int := 0;
  v_turn_id text;
  v_first   timestamptz;
  v_last    timestamptz;
  v_rows    int;
  t         record;
  c         record;
BEGIN
  -- a) Retry open turns.
  FOR t IN
    SELECT turn_id, wa_id FROM wa_turns
     WHERE (status = 'pending' OR (status = 'processing' AND claimed_at < now() - v_stale))
       AND (p_only_wa_ids IS NULL OR wa_id = ANY (p_only_wa_ids))
     ORDER BY created_at
  LOOP
    EXIT WHEN v_count >= v_limit;
    CONTINUE WHEN NOT pg_try_advisory_xact_lock(hashtext('wa-turn:' || t.wa_id));
    UPDATE wa_turns
       SET status = 'processing', claimed_at = now(), attempts = attempts + 1
     WHERE turn_id = t.turn_id
       AND (status = 'pending' OR (status = 'processing' AND claimed_at < now() - v_stale));
    CONTINUE WHEN NOT FOUND;
    UPDATE wa_inbound_messages SET processing_status = 'processing' WHERE turn_id = t.turn_id;
    v_result := v_result || jsonb_build_array(wa_turn_json(t.turn_id));
    v_count := v_count + 1;
  END LOOP;

  -- b) New turns.
  FOR c IN
    SELECT m.wa_id
      FROM wa_inbound_messages m
     WHERE m.processing_status = 'pending' AND m.turn_id IS NULL AND NOT m.is_group
       AND (p_only_wa_ids IS NULL OR m.wa_id = ANY (p_only_wa_ids))
     GROUP BY m.wa_id
    HAVING max(m.received_at) <= now() - v_quiet OR min(m.received_at) <= now() - v_max
     ORDER BY min(m.received_at)
  LOOP
    EXIT WHEN v_count >= v_limit;
    CONTINUE WHEN NOT pg_try_advisory_xact_lock(hashtext('wa-turn:' || c.wa_id));
    CONTINUE WHEN EXISTS (SELECT 1 FROM wa_turns
                           WHERE wa_id = c.wa_id AND status IN ('processing', 'pending'));

    -- Re-read under the lock.
    SELECT min(received_at), max(received_at) INTO v_first, v_last
      FROM wa_inbound_messages
     WHERE wa_id = c.wa_id AND processing_status = 'pending' AND turn_id IS NULL AND NOT is_group;
    CONTINUE WHEN v_first IS NULL;
    IF v_first > now() - v_max THEN
      CONTINUE WHEN v_last > now() - v_quiet;
      CONTINUE WHEN EXISTS (SELECT 1 FROM wa_inbound_messages m
                             WHERE m.wa_id = c.wa_id AND m.processing_status = 'pending'
                               AND m.turn_id IS NULL AND NOT m.is_group AND NOT wa_message_ready(m.id));
    ELSE
      -- Max wait reached: unfinished media and transcripts become failed (timeout) now, in the
      -- same transaction, so the database says exactly what the API is told.
      UPDATE wa_media w
         SET status = 'failed', last_error = jsonb_build_object('code', 'TIMEOUT', 'stage', 'download')
        FROM wa_inbound_messages m
       WHERE w.inbound_message_id = m.id AND m.wa_id = c.wa_id AND m.processing_status = 'pending'
         AND m.turn_id IS NULL AND w.status IN ('pending', 'downloading');
      UPDATE wa_media w
         SET transcript_status = 'failed',
             last_error = jsonb_build_object('code', 'TIMEOUT', 'stage', 'transcription')
        FROM wa_inbound_messages m
       WHERE w.inbound_message_id = m.id AND m.wa_id = c.wa_id AND m.processing_status = 'pending'
         AND m.turn_id IS NULL AND w.transcript_status = 'pending';
    END IF;

    v_turn_id := 'wt:' || gen_random_uuid();
    INSERT INTO wa_turns (turn_id, wa_id, status, claimed_at, attempts)
    VALUES (v_turn_id, c.wa_id, 'processing', now(), 1);
    UPDATE wa_inbound_messages
       SET processing_status = 'processing', turn_id = v_turn_id, last_error = NULL
     WHERE wa_id = c.wa_id AND processing_status = 'pending' AND turn_id IS NULL AND NOT is_group;
    GET DIAGNOSTICS v_rows = ROW_COUNT;
    IF v_rows = 0 THEN
      DELETE FROM wa_turns WHERE turn_id = v_turn_id;
      CONTINUE;
    END IF;
    v_result := v_result || jsonb_build_array(wa_turn_json(v_turn_id));
    v_count := v_count + 1;
  END LOOP;

  RETURN v_result;
END;
$$;
