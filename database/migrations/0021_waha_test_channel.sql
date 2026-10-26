-- 0021_waha_test_channel: TASK 02b (DECISIONS v1.19, T-03). WAHA (WhatsApp HTTP API, the free edition, engine NOWEB)
-- as a second, temporary channel for testing on a spare number until Meta approves the account. Nothing of the Meta
-- flow changes: whatsapp.provider chooses the channel and stays 'meta' unless the owner switches it.
--   The rules stay Meta's on both channels (the test recipients of 0006, the 24-hour window, the template approval,
--   idempotency, the dry run): wa_outbound_prepare runs first either way; only the transport differs.
--   app.waha_event     a WAHA webhook (message, message.ack) as Meta's webhook event, so app.wa_process_event takes
--                      it as it is (every row downstream, the turns and the API stay the same). A reply «1» to a
--                      numbered list becomes the press of that button (WhatsApp shows no buttons through WAHA).
--   app.waha_menus     the last numbered list sent to a number (24 hours), to read «1» back.
--   app.waha_sent      WAHA's answer to a send as Meta's (messages[0].id), and the list kept.
-- Ids: a WAHA message id becomes the wamid 'waha.' + id, a WAHA file the media id 'waha:' + its path on WAHA
-- (/api/files/…): n8n downloads it from waha.base_url only, never from an address in the event.

-- migrate:up
INSERT INTO app.settings (key, value, description) VALUES
  ('whatsapp.provider', 'meta', 'TASK 02b (v1.19): meta (production) or waha (the test channel on a spare number until Meta approves)'),
  ('waha.base_url', 'http://waha:3000', 'TASK 02b: WAHA inside the Docker network (never a public address)'),
  ('waha.session', 'default', 'TASK 02b: the WAHA session (one in the free edition)')
ON CONFLICT (key) DO NOTHING;

CREATE TABLE app.waha_menus (
  wa_id       text PRIMARY KEY CHECK (wa_id ~ '^[0-9]{8,15}$'),
  outbound_id uuid,
  kind        text NOT NULL CHECK (kind IN ('buttons', 'list')),
  choices     jsonb NOT NULL CHECK (jsonb_typeof(choices) = 'array'),  -- [{n, id, title}]
  sent_at     timestamptz NOT NULL DEFAULT now()
);

-- A WhatsApp id of a person (…@c.us, …@s.whatsapp.net) as digits; anything else (a group, a LID, a channel): NULL.
CREATE FUNCTION app.waha_wa_id(p_jid text) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
  SELECT CASE WHEN p_jid ~ '^[0-9]{8,15}@(c\.us|s\.whatsapp\.net)$' THEN split_part(p_jid, '@', 1) END
$$;

-- p_body: WAHA's webhook body {event, session, payload}. p_pn: the phone number n8n found for a sender that came as
-- a LID (…@lid, GET /api/{session}/lids/{lid}). Returns {event: <Meta's webhook value>} or {ignored: reason}.
CREATE FUNCTION app.waha_event(p_body jsonb, p_pn text DEFAULT NULL) RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  p        jsonb := p_body->'payload';
  v_kind   text := p_body->>'event';
  v_from   text := p->>'from';
  v_wa     text;
  v_id     text := p->>'id';
  v_ts     bigint := COALESCE(NULLIF(p->>'timestamp', '')::bigint, extract(epoch FROM now())::bigint);
  v_body   text := COALESCE(p->>'body', '');
  v_mime   text := lower(NULLIF(p->'media'->>'mimetype', ''));
  v_type   text;
  v_path   text;
  v_digits text;
  v_menu   app.waha_menus%ROWTYPE;
  v_choice jsonb;
  v_reply  text;
  v_msg    jsonb;
  v_status text;
BEGIN
  IF v_id IS NULL OR length(v_id) > 200 THEN
    RETURN jsonb_build_object('ignored', 'no_id');
  END IF;

  IF v_kind = 'message.ack' THEN
    -- Only our own messages have delivery statuses (Meta's statuses), and only those Meta knows.
    IF NOT COALESCE((p->>'fromMe')::boolean, false) THEN
      RETURN jsonb_build_object('ignored', 'not_ours');
    END IF;
    v_status := CASE (p->>'ack')::int WHEN 1 THEN 'sent' WHEN 2 THEN 'delivered' WHEN 3 THEN 'read' WHEN 4 THEN 'read'
                                      WHEN -1 THEN 'failed' END;
    IF v_status IS NULL THEN
      RETURN jsonb_build_object('ignored', 'pending');
    END IF;
    RETURN jsonb_build_object('event', jsonb_build_object(
      'messaging_product', 'whatsapp', 'metadata', jsonb_build_object('phone_number_id', 'waha'),
      'statuses', jsonb_build_array(jsonb_strip_nulls(jsonb_build_object(
        'id', 'waha.' || v_id, 'status', v_status, 'timestamp', extract(epoch FROM now())::bigint::text,
        'recipient_id', waha_wa_id(COALESCE(p->>'to', v_from)),
        'errors', CASE WHEN v_status = 'failed' THEN jsonb_build_array(jsonb_build_object('code', 0, 'title', 'WAHA_ACK_ERROR')) END)))));
  END IF;

  IF v_kind IS DISTINCT FROM 'message' THEN
    RETURN jsonb_build_object('ignored', 'event');
  END IF;
  IF COALESCE((p->>'fromMe')::boolean, false) THEN
    RETURN jsonb_build_object('ignored', 'from_me');
  END IF;
  IF v_from IS NULL OR v_from ~ '@(g\.us|newsletter|broadcast)$' THEN
    RETURN jsonb_build_object('ignored', 'not_a_person');
  END IF;
  v_wa := COALESCE(waha_wa_id(v_from), CASE WHEN v_from LIKE '%@lid' THEN COALESCE(waha_wa_id(p_pn), substring(p_pn FROM '^[0-9]{8,15}$')) END);
  IF v_wa IS NULL THEN
    RETURN jsonb_build_object('ignored', 'unknown_number');
  END IF;

  IF COALESCE((p->>'hasMedia')::boolean, false) THEN
    v_type := CASE WHEN v_mime LIKE 'image/webp%' THEN 'sticker'
                   WHEN v_mime LIKE 'image/%' THEN 'image'
                   WHEN v_mime LIKE 'video/%' THEN 'video'
                   WHEN v_mime LIKE 'audio/%' THEN 'audio'
                   ELSE 'document' END;
    -- Only the path on WAHA is kept; n8n adds waha.base_url (never an address from the event).
    v_path := substring(p->'media'->>'url' FROM '^https?://[^/]+(/api/files/[A-Za-z0-9._@=/-]+)$');
    v_msg := jsonb_build_object('type', v_type, v_type, jsonb_strip_nulls(jsonb_build_object(
      'id', 'waha:' || COALESCE(v_path, ''),
      'mime_type', v_mime,
      'caption', NULLIF(btrim(v_body), ''),
      'filename', CASE WHEN v_type = 'document' THEN left(p->'media'->>'filename', 200) END,
      'voice', CASE WHEN v_type = 'audio' THEN v_mime LIKE 'audio/ogg%' END)));
  ELSIF p->'location' ? 'latitude' THEN
    v_msg := jsonb_build_object('type', 'location', 'location', jsonb_build_object(
      'latitude', p->'location'->'latitude', 'longitude', p->'location'->'longitude'));
  ELSIF btrim(v_body) <> '' THEN
    -- A number answering the last list sent to this number (Latin or Arabic-Indic digits) is the press of that row.
    v_digits := translate(btrim(v_body), '٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', '01234567890123456789');
    IF v_digits ~ '^[0-9]{1,2}$' THEN
      SELECT * INTO v_menu FROM waha_menus WHERE wa_id = v_wa AND sent_at > now() - interval '24 hours';
      IF FOUND THEN
        SELECT c INTO v_choice FROM jsonb_array_elements(v_menu.choices) c WHERE (c->>'n')::int = v_digits::int LIMIT 1;
      END IF;
    END IF;
    IF v_choice IS NOT NULL THEN
      v_reply := CASE WHEN v_menu.kind = 'list' THEN 'list_reply' ELSE 'button_reply' END;
      v_msg := jsonb_build_object('type', 'interactive', 'interactive', jsonb_build_object(
        'type', v_reply, v_reply, jsonb_build_object('id', v_choice->>'id', 'title', v_choice->>'title')));
    ELSE
      v_msg := jsonb_build_object('type', 'text', 'text', jsonb_build_object('body', v_body));
    END IF;
  ELSE
    v_msg := jsonb_build_object('type', 'unsupported');
  END IF;

  v_msg := v_msg || jsonb_build_object('from', v_wa, 'id', 'waha.' || v_id, 'timestamp', v_ts::text);
  RETURN jsonb_build_object('event', jsonb_build_object(
    'messaging_product', 'whatsapp', 'metadata', jsonb_build_object('phone_number_id', 'waha'),
    'contacts', jsonb_build_array(jsonb_build_object('wa_id', v_wa, 'profile', jsonb_strip_nulls(jsonb_build_object(
      'name', left(COALESCE(p->'_data'->>'pushName', p->'_data'->>'notifyName'), 120))))),
    'messages', jsonb_build_array(v_msg)));
END;
$$;

-- WAHA's answer to a send (after wa_outbound_prepare allowed it), as Meta's answer for wa_outbound_result. A numbered
-- list that went out is kept for its number (p_choices [{n, id, title}], p_kind buttons|list). A text or a document of a
-- LATER reply ends it, so a number typed after Hazem moved on is words again; one in the same reply (the offer, then
-- «what is it about?») does not. The reply is the idempotency key without its last part (hazem:<turn>:<n>).
CREATE FUNCTION app.waha_sent(p_outbound_id uuid, p_wa_id text, p_kind text, p_choices jsonb, p_status int, p_body jsonb)
RETURNS jsonb
LANGUAGE plpgsql
SET search_path = app, public
AS $$
DECLARE
  -- WEBJS: id._serialized or id; NOWEB: key.id (its webhook ids are «true_<chat>_<id>»).
  v_id text := COALESCE(p_body->'id'->>'_serialized', CASE WHEN jsonb_typeof(p_body->'id') = 'string' THEN p_body->>'id' END,
                        CASE WHEN p_body->'key'->>'id' IS NOT NULL THEN 'true_' || p_wa_id || '@c.us_' || (p_body->'key'->>'id') END);
BEGIN
  IF p_status BETWEEN 200 AND 299 AND v_id IS NOT NULL THEN
    IF p_kind IN ('buttons', 'list') AND jsonb_typeof(p_choices) = 'array' AND jsonb_array_length(p_choices) > 0 THEN
      INSERT INTO waha_menus (wa_id, outbound_id, kind, choices, sent_at)
      VALUES (p_wa_id, p_outbound_id, p_kind, p_choices, now())
      ON CONFLICT (wa_id) DO UPDATE SET outbound_id = EXCLUDED.outbound_id, kind = EXCLUDED.kind,
                                        choices = EXCLUDED.choices, sent_at = EXCLUDED.sent_at;
    ELSE
      DELETE FROM waha_menus m
       WHERE m.wa_id = p_wa_id
         AND (SELECT regexp_replace(o.idempotency_key, ':[0-9]+$', '') FROM wa_outbound_messages o WHERE o.id = m.outbound_id)
             IS DISTINCT FROM
             (SELECT regexp_replace(o.idempotency_key, ':[0-9]+$', '') FROM wa_outbound_messages o WHERE o.id = p_outbound_id);
    END IF;
    RETURN jsonb_build_object('statusCode', 200, 'body', jsonb_build_object(
      'messaging_product', 'whatsapp', 'messages', jsonb_build_array(jsonb_build_object('id', 'waha.' || v_id))));
  END IF;
  -- No Meta error code: wa_outbound_result maps it to NETWORK_OR_UNKNOWN (the API opens its task as for any failure).
  RETURN jsonb_build_object('statusCode', COALESCE(p_status, 0), 'body', jsonb_build_object(
    'error', jsonb_build_object('message', 'WAHA answered ' || COALESCE(p_status::text, 'nothing'))));
END;
$$;

REVOKE ALL ON FUNCTION app.waha_wa_id(text), app.waha_event(jsonb, text), app.waha_sent(uuid, text, text, jsonb, int, jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION app.waha_wa_id(text), app.waha_event(jsonb, text), app.waha_sent(uuid, text, text, jsonb, int, jsonb) TO office_app;

-- migrate:down
DROP FUNCTION IF EXISTS app.waha_sent(uuid, text, text, jsonb, int, jsonb);
DROP FUNCTION IF EXISTS app.waha_event(jsonb, text);
DROP FUNCTION IF EXISTS app.waha_wa_id(text);
DROP TABLE IF EXISTS app.waha_menus;
DELETE FROM app.settings WHERE key IN ('whatsapp.provider', 'waha.base_url', 'waha.session');
