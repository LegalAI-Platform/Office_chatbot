-- 0004_turn_grouping_media: TASK 03 — the 8-second grouping of client messages into one turn,
-- media download records, and voice transcripts (DECISIONS v1.3, TASK-03-prompt.md).
--   * app.wa_turns: one row per turn ('wt:<uuid>'); at most ONE open turn (pending or
--     processing) per client, enforced by a partial unique index, so replies keep their order.
--   * app.wa_media: one row per media message (download state, S3 location, transcript).
--   * wa_turns_claim / wa_turns_finish replaced; new wa_media_* functions. n8n only calls them.
-- Every time rule uses received_at (our clock), never sent_at (Meta's clock).

-- migrate:up

-- ---------------------------------------------------------------------------
-- Settings
-- ---------------------------------------------------------------------------
INSERT INTO app.settings (key, value, description) VALUES
  ('whatsapp.group_quiet_seconds', '8',   'A turn closes after this many seconds without a new message from the client'),
  ('whatsapp.group_max_seconds',   '60',  'A turn closes at most this many seconds after its first message (also the media wait limit)'),
  ('whatsapp.turn_stale_seconds',  '300', 'A turn or download left in processing/downloading this long is taken again. Must be 300 outside tests'),
  ('whatsapp.media_max_mb',        '50',  'Larger files are not downloaded (status too_large)'),
  ('whatsapp.graph_base_url',      'https://graph.facebook.com', 'Graph API base URL. Only hazem-e2e points it at the test stub'),
  ('ai.openai_base_url',           'https://api.openai.com/v1',  'OpenAI base URL. Only hazem-e2e points it at the test stub'),
  ('ai.transcription_model',       'whisper-1', 'Voice transcription model. Default only: the owner confirms it after Egyptian samples (T-08)');

-- ---------------------------------------------------------------------------
-- Turns
-- ---------------------------------------------------------------------------
CREATE TABLE app.wa_turns (
  turn_id    text PRIMARY KEY CHECK (turn_id ~ '^wt:[0-9a-f-]{36}$'),
  wa_id      text NOT NULL,
  status     text NOT NULL CHECK (status IN ('processing', 'pending', 'processed', 'failed')),
  claimed_at timestamptz,
  attempts   int NOT NULL DEFAULT 0,
  last_error jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);
-- Ordering guarantee: a client never has two open turns.
CREATE UNIQUE INDEX wa_turns_one_open_per_client ON app.wa_turns (wa_id)
  WHERE status IN ('processing', 'pending');
CREATE INDEX wa_turns_status_idx ON app.wa_turns (status, claimed_at);
CREATE TRIGGER trg_wa_turns_updated_at BEFORE UPDATE ON app.wa_turns
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

ALTER TABLE app.wa_inbound_messages ADD COLUMN turn_id text REFERENCES app.wa_turns (turn_id);
CREATE INDEX wa_inbound_messages_turn_idx ON app.wa_inbound_messages (turn_id);
CREATE INDEX wa_inbound_messages_pending_idx ON app.wa_inbound_messages (wa_id, received_at)
  WHERE processing_status = 'pending' AND turn_id IS NULL;

-- ---------------------------------------------------------------------------
-- Media
-- ---------------------------------------------------------------------------
CREATE TABLE app.wa_media (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  inbound_message_id uuid NOT NULL UNIQUE REFERENCES app.wa_inbound_messages (id) ON DELETE CASCADE,
  meta_media_id      text,
  kind               text NOT NULL CHECK (kind IN ('image', 'document', 'audio', 'video', 'sticker')),
  voice              boolean NOT NULL DEFAULT false,
  mime_type          text,
  file_size          bigint,
  sha256             text,
  filename           text,   -- original document name (may hold personal data): never in keys or logs
  bucket             text CHECK (bucket IN ('office-media', 'office-sensitive')),
  object_key         text,
  status             text NOT NULL DEFAULT 'pending'
    CHECK (status IN ('pending', 'downloading', 'stored', 'failed', 'too_large', 'rejected')),
  attempts           int NOT NULL DEFAULT 0,
  claimed_at         timestamptz,
  last_error         jsonb,
  stored_at          timestamptz,
  transcript         text,
  transcript_status  text NOT NULL DEFAULT 'not_needed'
    CHECK (transcript_status IN ('not_needed', 'pending', 'done', 'failed')),
  transcript_model   text,
  transcribed_at     timestamptz,
  created_at         timestamptz NOT NULL DEFAULT now(),
  updated_at         timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT wa_media_stored_has_location CHECK (status <> 'stored' OR (bucket IS NOT NULL AND object_key IS NOT NULL))
);
CREATE INDEX wa_media_status_idx ON app.wa_media (status, claimed_at);
CREATE TRIGGER trg_wa_media_updated_at BEFORE UPDATE ON app.wa_media
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- ---------------------------------------------------------------------------
-- Small helpers
-- ---------------------------------------------------------------------------
CREATE FUNCTION app.wa_setting_int(p_key text, p_default int) RETURNS int
LANGUAGE sql STABLE SET search_path = app, public AS $$
  SELECT COALESCE((SELECT NULLIF(value, '')::int FROM settings WHERE key = p_key), p_default)
$$;

-- Bare MIME type: "audio/ogg; codecs=opus" -> "audio/ogg".
CREATE FUNCTION app.wa_mime_base(p_mime text) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
  SELECT NULLIF(lower(btrim(split_part(COALESCE(p_mime, ''), ';', 1))), '')
$$;

-- Allowed media types and their file extension; anything else is rejected (never stored).
CREATE FUNCTION app.wa_media_extension(p_mime text) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
  SELECT CASE app.wa_mime_base(p_mime)
    WHEN 'image/jpeg' THEN 'jpg'   WHEN 'image/png'  THEN 'png'   WHEN 'image/webp' THEN 'webp'
    WHEN 'image/heic' THEN 'heic'
    WHEN 'application/pdf' THEN 'pdf'
    WHEN 'application/msword' THEN 'doc'
    WHEN 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' THEN 'docx'
    WHEN 'application/vnd.ms-excel' THEN 'xls'
    WHEN 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' THEN 'xlsx'
    WHEN 'application/vnd.ms-powerpoint' THEN 'ppt'
    WHEN 'application/vnd.openxmlformats-officedocument.presentationml.presentation' THEN 'pptx'
    WHEN 'text/plain' THEN 'txt'
    WHEN 'audio/ogg'  THEN 'ogg'   WHEN 'audio/mpeg' THEN 'mp3'   WHEN 'audio/mp4' THEN 'm4a'
    WHEN 'audio/aac'  THEN 'aac'   WHEN 'audio/amr'  THEN 'amr'
    WHEN 'video/mp4'  THEN 'mp4'   WHEN 'video/3gpp' THEN '3gp'
    ELSE NULL END
$$;

CREATE FUNCTION app.wa_is_media_type(p_type text) RETURNS boolean
LANGUAGE sql IMMUTABLE AS $$
  SELECT p_type IN ('image', 'document', 'audio', 'video', 'sticker')
$$;

-- A message may join a turn once its media (if any) is final and its transcript (if any) is settled.
CREATE FUNCTION app.wa_message_ready(p_message_id uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path = app, public AS $$
  SELECT NOT EXISTS (
    SELECT 1 FROM wa_media w
     WHERE w.inbound_message_id = p_message_id
       AND (w.status IN ('pending', 'downloading') OR w.transcript_status = 'pending'))
$$;

-- The message as the API receives it (docs/hazem-internal-api.md). A text message keeps
-- exactly the TASK 02 shape. No URL and no file content ever (T-09).
CREATE FUNCTION app.wa_message_json(p_message_id uuid) RETURNS jsonb
LANGUAGE plpgsql STABLE SET search_path = app, public AS $$
DECLARE
  m   wa_inbound_messages%ROWTYPE;
  w   wa_media%ROWTYPE;
  r   jsonb;
  t   text;
  base jsonb;
  media jsonb;
BEGIN
  SELECT * INTO m FROM wa_inbound_messages WHERE id = p_message_id;
  r := m.raw;
  t := m.message_type;
  base := jsonb_build_object('wamid', m.wamid, 'sent_at', m.sent_at, 'context_wamid', m.context_wamid);

  IF t = 'text' THEN
    RETURN base || jsonb_build_object('type', 'text', 'text', r->'text'->>'body');
  END IF;

  IF wa_is_media_type(t) THEN
    SELECT * INTO w FROM wa_media WHERE inbound_message_id = p_message_id;
    media := jsonb_strip_nulls(jsonb_build_object(
      'status',     COALESCE(w.status, 'failed'),
      'bucket',     CASE WHEN w.status = 'stored' THEN w.bucket END,
      'key',        CASE WHEN w.status = 'stored' THEN w.object_key END,
      'mime_type',  wa_mime_base(COALESCE(w.mime_type, r->t->>'mime_type')),
      'size_bytes', w.file_size,
      'sha256',     w.sha256,
      'filename',   CASE WHEN t = 'document' THEN w.filename END,
      'voice',      CASE WHEN t = 'audio' THEN COALESCE(w.voice, false) END));
    base := base || jsonb_build_object('type', t, 'media', media);
    IF t IN ('image', 'document', 'video') THEN
      base := base || jsonb_build_object('caption', r->t->>'caption');
    END IF;
    IF t = 'audio' AND w.transcript_status IN ('done', 'failed') THEN
      base := base || jsonb_build_object('transcript', jsonb_build_object(
        'status', w.transcript_status, 'text', w.transcript, 'language', 'ar'));
    END IF;
    RETURN base;
  END IF;

  IF t = 'location' THEN
    RETURN base || jsonb_build_object('type', 'location', 'location', jsonb_build_object(
      'latitude',  (r->'location'->>'latitude')::numeric,
      'longitude', (r->'location'->>'longitude')::numeric,
      'name',      r->'location'->>'name',
      'address',   r->'location'->>'address'));
  END IF;
  IF t = 'reaction' THEN
    RETURN base || jsonb_build_object('type', 'reaction', 'reaction', jsonb_build_object(
      'emoji', r->'reaction'->>'emoji', 'message_wamid', r->'reaction'->>'message_id'));
  END IF;
  IF t = 'interactive' THEN
    RETURN base || jsonb_build_object('type', 'interactive', 'interactive', jsonb_build_object(
      'kind',  r->'interactive'->>'type',
      'id',    COALESCE(r->'interactive'->'button_reply'->>'id', r->'interactive'->'list_reply'->>'id'),
      'title', COALESCE(r->'interactive'->'button_reply'->>'title', r->'interactive'->'list_reply'->>'title')));
  END IF;
  IF t = 'button' THEN
    RETURN base || jsonb_build_object('type', 'button', 'button', jsonb_build_object(
      'payload', r->'button'->>'payload', 'text', r->'button'->>'text'));
  END IF;

  RETURN base || jsonb_build_object('type', 'unsupported', 'original_type', t);
END;
$$;

CREATE FUNCTION app.wa_turn_json(p_turn_id text) RETURNS jsonb
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

-- ---------------------------------------------------------------------------
-- wa_process_event: same behaviour as 0002, plus one wa_media row per new media message
-- ---------------------------------------------------------------------------
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

-- ---------------------------------------------------------------------------
-- Turn claim / finish (replace the 0003 versions)
-- ---------------------------------------------------------------------------
DROP FUNCTION app.wa_turns_claim(int, text[]);
DROP FUNCTION app.wa_turns_finish(text[], text, jsonb);

-- Returns a JSON array of turns ready for the API:
--  a) open turns to retry first (pending = given back after an API failure, or processing for
--     longer than whatsapp.turn_stale_seconds): same turn_id, same messages, nothing new added;
--  b) then new turns: clients whose waiting messages are quiet for group_quiet_seconds (or whose
--     first waiting message is group_max_seconds old), whose media is final, and who have no
--     open turn. At the max limit unfinished media is marked failed (timeout) in this transaction.
-- One per-client advisory lock (pg_try_: a locked client is skipped, never waited for).
-- p_only_wa_ids limits the claim to some numbers (tests only).
CREATE FUNCTION app.wa_turns_claim(p_limit int DEFAULT 20, p_only_wa_ids text[] DEFAULT NULL)
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

-- Finishes a claimed turn and its messages: processed | failed | pending (give back for a later
-- retry with the same turn_id). Only a turn still in 'processing' changes; returns the number of
-- messages updated (0 = nothing changed).
CREATE FUNCTION app.wa_turns_finish(p_turn_id text, p_status text, p_error jsonb DEFAULT NULL)
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

  UPDATE wa_turns SET status = p_status, last_error = p_error
   WHERE turn_id = p_turn_id AND status = 'processing';
  IF NOT FOUND THEN
    RETURN 0;
  END IF;

  UPDATE wa_inbound_messages
     SET processing_status = p_status,
         processed_at = CASE WHEN p_status = 'pending' THEN NULL ELSE now() END,
         last_error = p_error
   WHERE turn_id = p_turn_id;
  GET DIAGNOSTICS v_count = ROW_COUNT;
  RETURN v_count;
END;
$$;

-- ---------------------------------------------------------------------------
-- Media download steps (WA-MEDIA-FETCH / WA-MEDIA-ONE call these; all rules live here)
-- ---------------------------------------------------------------------------
-- Claims media to download: pending, or downloading for longer than turn_stale_seconds, and
-- whose message is not in a turn yet. A download is tried at most 3 times.
CREATE FUNCTION app.wa_media_claim(p_limit int DEFAULT 10, p_only_wa_ids text[] DEFAULT NULL)
RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  v_stale interval := make_interval(secs => wa_setting_int('whatsapp.turn_stale_seconds', 300));
  v_result jsonb;
BEGIN
  UPDATE wa_media w
     SET status = 'failed', last_error = jsonb_build_object('code', 'TOO_MANY_ATTEMPTS', 'attempts', w.attempts)
    FROM wa_inbound_messages m
   WHERE w.inbound_message_id = m.id AND m.turn_id IS NULL AND w.attempts >= 3
     AND (w.status = 'pending' OR (w.status = 'downloading' AND w.claimed_at < now() - v_stale))
     AND (p_only_wa_ids IS NULL OR m.wa_id = ANY (p_only_wa_ids));

  WITH picked AS (
    SELECT w.id
      FROM wa_media w JOIN wa_inbound_messages m ON m.id = w.inbound_message_id
     WHERE m.turn_id IS NULL AND m.processing_status = 'pending'
       AND (w.status = 'pending' OR (w.status = 'downloading' AND w.claimed_at < now() - v_stale))
       AND (p_only_wa_ids IS NULL OR m.wa_id = ANY (p_only_wa_ids))
     ORDER BY m.received_at
     LIMIT GREATEST(LEAST(COALESCE(p_limit, 10), 50), 1)
     FOR UPDATE OF w SKIP LOCKED
  ), claimed AS (
    UPDATE wa_media w
       SET status = 'downloading', attempts = w.attempts + 1, claimed_at = now()
      FROM picked WHERE w.id = picked.id
    RETURNING w.id, w.meta_media_id, w.kind, w.voice
  )
  SELECT COALESCE(jsonb_agg(jsonb_build_object(
           'media_id', id, 'meta_media_id', meta_media_id, 'kind', kind, 'voice', voice)), '[]'::jsonb)
    INTO v_result FROM claimed;
  RETURN v_result;
END;
$$;

-- Step 1 result (Graph API media metadata, WITHOUT keeping its URL): decides whether to download.
-- Returns {action: download|too_large|rejected|failed|skip, bucket, folder, file_name, key,
--          expected_sha256, mime_type}. The bucket comes from a fixed safety rule (2.2):
-- image/document -> office-sensitive, audio/video/sticker -> office-media.
CREATE FUNCTION app.wa_media_precheck(p_media_id uuid, p_meta jsonb) RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  w        wa_media%ROWTYPE;
  v_recv   timestamptz;
  v_mime   text;
  v_size   bigint;
  v_max    bigint := wa_setting_int('whatsapp.media_max_mb', 50)::bigint * 1024 * 1024;
  v_ext    text;
  v_bucket text;
  v_folder text;
BEGIN
  SELECT * INTO w FROM wa_media WHERE id = p_media_id FOR UPDATE;
  IF NOT FOUND OR w.status <> 'downloading' THEN
    RETURN jsonb_build_object('action', 'skip');
  END IF;
  SELECT received_at INTO v_recv FROM wa_inbound_messages WHERE id = w.inbound_message_id;

  v_mime := COALESCE(NULLIF(p_meta->>'mime_type', ''), w.mime_type);
  v_size := NULLIF(p_meta->>'file_size', '')::bigint;
  UPDATE wa_media SET mime_type = v_mime, file_size = v_size,
         sha256 = COALESCE(NULLIF(p_meta->>'sha256', ''), w.sha256)
   WHERE id = p_media_id;

  IF v_size IS NOT NULL AND v_size > v_max THEN
    UPDATE wa_media SET status = 'too_large',
           last_error = jsonb_build_object('code', 'TOO_LARGE', 'size_bytes', v_size, 'max_bytes', v_max)
     WHERE id = p_media_id;
    RETURN jsonb_build_object('action', 'too_large');
  END IF;

  v_ext := wa_media_extension(v_mime);
  IF v_ext IS NULL THEN
    UPDATE wa_media SET status = 'rejected',
           last_error = jsonb_build_object('code', 'MIME_NOT_ALLOWED', 'mime_type', wa_mime_base(v_mime))
     WHERE id = p_media_id;
    RETURN jsonb_build_object('action', 'rejected');
  END IF;

  IF COALESCE(p_meta->>'url', '') = '' THEN
    UPDATE wa_media SET status = 'failed', last_error = jsonb_build_object('code', 'NO_MEDIA_URL')
     WHERE id = p_media_id;
    RETURN jsonb_build_object('action', 'failed');
  END IF;

  v_bucket := CASE WHEN w.kind IN ('image', 'document') THEN 'office-sensitive' ELSE 'office-media' END;
  -- The media row id, never the wamid: a wamid encodes the client's phone number.
  v_folder := 'wa/' || to_char(v_recv AT TIME ZONE 'Africa/Cairo', 'YYYY/MM');
  RETURN jsonb_build_object(
    'action', 'download', 'bucket', v_bucket, 'folder', v_folder,
    'file_name', w.id || '.' || v_ext, 'key', v_folder || '/' || w.id || '.' || v_ext,
    'expected_sha256', lower(COALESCE(NULLIF(p_meta->>'sha256', ''), w.sha256, '')),
    'mime_type', wa_mime_base(v_mime), 'kind', w.kind, 'voice', w.voice);
END;
$$;

-- Step 2: the downloaded file's sha256 (hex, from n8n). Mismatch -> failed. Returns {ok}.
CREATE FUNCTION app.wa_media_verify(p_media_id uuid, p_sha256 text) RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  w wa_media%ROWTYPE;
BEGIN
  SELECT * INTO w FROM wa_media WHERE id = p_media_id FOR UPDATE;
  IF NOT FOUND OR w.status <> 'downloading' THEN
    RETURN jsonb_build_object('ok', false, 'reason', 'not_downloading');
  END IF;
  -- Meta's sha256 may come as hex or as base64 of the digest; n8n computes hex.
  IF COALESCE(w.sha256, '') <> ''
     AND lower(w.sha256) <> lower(COALESCE(p_sha256, ''))
     AND w.sha256 <> COALESCE(encode(decode(NULLIF(lower(p_sha256), ''), 'hex'), 'base64'), '') THEN
    UPDATE wa_media SET status = 'failed',
           last_error = jsonb_build_object('code', 'SHA256_MISMATCH')
     WHERE id = p_media_id;
    RETURN jsonb_build_object('ok', false, 'reason', 'sha256_mismatch');
  END IF;
  UPDATE wa_media SET sha256 = lower(p_sha256) WHERE id = p_media_id;
  RETURN jsonb_build_object('ok', true);
END;
$$;

-- Step 3: the file is in S3. Refused (false) unless the row is still downloading, e.g. after
-- the turn already went out with this media marked failed (timeout). Audio gets a pending
-- transcript.
CREATE FUNCTION app.wa_media_stored(p_media_id uuid, p_bucket text, p_key text) RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  w wa_media%ROWTYPE;
BEGIN
  UPDATE wa_media
     SET status = 'stored', bucket = p_bucket, object_key = p_key, stored_at = now(), last_error = NULL,
         transcript_status = CASE WHEN kind = 'audio' THEN 'pending' ELSE transcript_status END
   WHERE id = p_media_id AND status = 'downloading'
  RETURNING * INTO w;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('ok', false, 'reason', 'not_downloading');
  END IF;
  RETURN jsonb_build_object('ok', true, 'kind', w.kind, 'mime_type', wa_mime_base(w.mime_type),
                            'transcribe', w.kind = 'audio' AND wa_mime_base(w.mime_type) LIKE 'audio/%');
END;
$$;

CREATE FUNCTION app.wa_media_failed(p_media_id uuid, p_error jsonb) RETURNS boolean
LANGUAGE plpgsql
SET search_path = app, public
AS $$
BEGIN
  UPDATE wa_media SET status = 'failed', last_error = COALESCE(p_error, '{}'::jsonb)
   WHERE id = p_media_id AND status = 'downloading';
  RETURN FOUND;
END;
$$;

-- Step 4 (audio only): transcript result. Only a pending transcript changes.
CREATE FUNCTION app.wa_media_transcribed(p_media_id uuid, p_ok boolean, p_text text,
                                         p_model text, p_error jsonb DEFAULT NULL)
RETURNS boolean
LANGUAGE plpgsql
SET search_path = app, public
AS $$
BEGIN
  UPDATE wa_media
     SET transcript_status = CASE WHEN p_ok AND COALESCE(btrim(p_text), '') <> '' THEN 'done' ELSE 'failed' END,
         transcript = CASE WHEN p_ok THEN left(p_text, 16000) END,
         transcript_model = p_model,
         transcribed_at = now(),
         last_error = CASE WHEN p_ok AND COALESCE(btrim(p_text), '') <> '' THEN last_error
                           ELSE COALESCE(p_error, jsonb_build_object('code', 'EMPTY_TRANSCRIPT')) END
   WHERE id = p_media_id AND transcript_status = 'pending';
  RETURN FOUND;
END;
$$;

-- ---------------------------------------------------------------------------
-- Rows in flight when 0004 is applied
-- ---------------------------------------------------------------------------
-- Text messages claimed under the 0003 scheme (turn_id 'wa:<wamid>'): back to pending so they
-- are grouped again. A repeated reply is impossible: the API answers a new turn_id, but n8n's
-- idempotency keys and the processed state of the old hazem.turns row are untouched.
UPDATE app.wa_inbound_messages SET processing_status = 'pending', last_error = NULL
 WHERE processing_status = 'processing' AND turn_id IS NULL;
-- Media that arrived before TASK 03 was never downloaded and Meta's URLs have expired.
INSERT INTO app.wa_media (inbound_message_id, meta_media_id, kind, voice, mime_type, filename, status, last_error)
SELECT m.id, m.raw->m.message_type->>'id', m.message_type,
       m.message_type = 'audio' AND COALESCE((m.raw->'audio'->>'voice')::boolean, false),
       m.raw->m.message_type->>'mime_type',
       CASE WHEN m.message_type = 'document' THEN m.raw->'document'->>'filename' END,
       'failed', jsonb_build_object('code', 'RECEIVED_BEFORE_TASK03')
  FROM app.wa_inbound_messages m
 WHERE app.wa_is_media_type(m.message_type) AND NOT m.is_group
   AND NOT EXISTS (SELECT 1 FROM app.wa_media w WHERE w.inbound_message_id = m.id);

-- ---------------------------------------------------------------------------
-- Permissions: only n8n's role calls these
-- ---------------------------------------------------------------------------
REVOKE ALL ON FUNCTION app.wa_turns_claim(int, text[]) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_turns_finish(text, text, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_media_claim(int, text[]) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_media_precheck(uuid, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_media_verify(uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_media_stored(uuid, text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_media_failed(uuid, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION app.wa_media_transcribed(uuid, boolean, text, text, jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION app.wa_turns_claim(int, text[]) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_turns_finish(text, text, jsonb) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_media_claim(int, text[]) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_media_precheck(uuid, jsonb) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_media_verify(uuid, text) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_media_stored(uuid, text, text) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_media_failed(uuid, jsonb) TO office_app;
GRANT EXECUTE ON FUNCTION app.wa_media_transcribed(uuid, boolean, text, text, jsonb) TO office_app;

-- migrate:down
DROP FUNCTION IF EXISTS app.wa_media_transcribed(uuid, boolean, text, text, jsonb);
DROP FUNCTION IF EXISTS app.wa_media_failed(uuid, jsonb);
DROP FUNCTION IF EXISTS app.wa_media_stored(uuid, text, text);
DROP FUNCTION IF EXISTS app.wa_media_verify(uuid, text);
DROP FUNCTION IF EXISTS app.wa_media_precheck(uuid, jsonb);
DROP FUNCTION IF EXISTS app.wa_media_claim(int, text[]);
DROP FUNCTION IF EXISTS app.wa_turns_finish(text, text, jsonb);
DROP FUNCTION IF EXISTS app.wa_turns_claim(int, text[]);

-- Messages that belonged to a turn: back to the 0003 world (one text message = one turn).
UPDATE app.wa_inbound_messages SET processing_status = 'pending', processed_at = NULL
 WHERE processing_status = 'processing';

-- wa_process_event as in 0002 (no media rows).
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

DROP FUNCTION IF EXISTS app.wa_turn_json(text);
DROP FUNCTION IF EXISTS app.wa_message_json(uuid);
DROP FUNCTION IF EXISTS app.wa_message_ready(uuid);
DROP FUNCTION IF EXISTS app.wa_is_media_type(text);
DROP FUNCTION IF EXISTS app.wa_media_extension(text);
DROP FUNCTION IF EXISTS app.wa_mime_base(text);
DROP FUNCTION IF EXISTS app.wa_setting_int(text, int);
DROP TABLE IF EXISTS app.wa_media;
ALTER TABLE app.wa_inbound_messages DROP COLUMN IF EXISTS turn_id;
DROP TABLE IF EXISTS app.wa_turns;
DELETE FROM app.settings WHERE key IN
  ('whatsapp.group_quiet_seconds', 'whatsapp.group_max_seconds', 'whatsapp.turn_stale_seconds',
   'whatsapp.media_max_mb', 'whatsapp.graph_base_url', 'ai.openai_base_url', 'ai.transcription_model');

-- The 0003 hand-off functions.
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
