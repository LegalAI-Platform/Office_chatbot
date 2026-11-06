-- 0065_album_wrapper: DECISIONS v1.81. Several photos sent at once arrive as one image message each AND as WhatsApp's album
-- wrapper, which Meta reports as type «unsupported» (raw.unsupported.type = «album», error 131051). The wrapper carries no
-- photo. Its message JSON now says original_type «album» (it said «unsupported»), so the responder leaves it out and never
-- answers «this kind of message is not supported» for photos that did arrive. The 0008 function is kept and wrapped.

-- migrate:up
ALTER FUNCTION app.wa_message_json(uuid) RENAME TO wa_message_json_0008;

CREATE FUNCTION app.wa_message_json(p_message_id uuid) RETURNS jsonb
LANGUAGE sql STABLE SET search_path = app, public AS $$
  SELECT wa_message_json_0008(m.id)
         || CASE WHEN m.message_type = 'unsupported' AND m.raw->'unsupported'->>'type' = 'album'
                 THEN jsonb_build_object('original_type', 'album') ELSE '{}'::jsonb END
    FROM wa_inbound_messages m WHERE m.id = p_message_id
$$;

-- migrate:down
DROP FUNCTION IF EXISTS app.wa_message_json(uuid);
ALTER FUNCTION app.wa_message_json_0008(uuid) RENAME TO wa_message_json;
