-- 0072_chatwoot_early: the owner (2026-11-08, «ليه مش حاسس ان الشات ووت ريل تايم؟»). A client's message reached Chatwoot only
-- with its turn, after Hazem answered: 16–27 s after he sent it (measured on the test server), and the staff saw his message
-- and Hazem's answer at once. Now his words go to Chatwoot as soon as n8n stores them, and the turn adds Hazem's answer.
--
-- The API still never reads the WhatsApp tables (schema app, the healthcheck's rule): it calls one function that gives back
-- only the words of the last ten minutes' text messages and presses, nothing else of a row. hazem.chatwoot_inbound keeps
-- the ones already in Chatwoot, so the turn does not post them a second time.

-- migrate:up
CREATE TABLE hazem.chatwoot_inbound (
  wamid     text PRIMARY KEY,
  wa_id     text NOT NULL CHECK (wa_id ~ '^[0-9]{8,15}$'),
  posted_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX chatwoot_inbound_wa_id_idx ON hazem.chatwoot_inbound (wa_id);
GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.chatwoot_inbound TO office_api;
GRANT SELECT ON hazem.chatwoot_inbound TO office_readonly;

CREATE FUNCTION app.wa_inbound_early(p_since timestamptz, p_limit int)
RETURNS TABLE (wamid text, wa_id text, contact_name text, kind text, body text)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = app, pg_temp AS $$
  SELECT m.wamid, m.wa_id, m.contact_name, m.message_type,
         CASE m.message_type
           WHEN 'text' THEN m.raw #>> '{text,body}'
           WHEN 'interactive' THEN COALESCE(m.raw #>> '{interactive,button_reply,title}', m.raw #>> '{interactive,list_reply,title}')
           WHEN 'button' THEN m.raw #>> '{button,text}'
         END
    FROM app.wa_inbound_messages m
   WHERE m.received_at >= p_since AND m.received_at > now() - interval '10 minutes'
     AND NOT m.is_group AND m.processing_status NOT LIKE 'ignored%'
     AND m.message_type IN ('text', 'interactive', 'button')
   ORDER BY m.received_at
   LIMIT LEAST(GREATEST(p_limit, 1), 100)
$$;
REVOKE ALL ON FUNCTION app.wa_inbound_early(timestamptz, int) FROM PUBLIC;
GRANT USAGE ON SCHEMA app TO office_api; -- the function only: office_api is granted no table of schema app
GRANT EXECUTE ON FUNCTION app.wa_inbound_early(timestamptz, int) TO office_api;

-- migrate:down
REVOKE EXECUTE ON FUNCTION app.wa_inbound_early(timestamptz, int) FROM office_api;
REVOKE USAGE ON SCHEMA app FROM office_api;
DROP FUNCTION app.wa_inbound_early(timestamptz, int);
DROP TABLE hazem.chatwoot_inbound;
