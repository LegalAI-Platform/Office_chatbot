-- 0061_wa_hazem_answers: DECISIONS v1.69. The owner (2026-11-05): «ليه مش ريل تايم؟». The blue ticks and «بيكتب…» went only after the
-- 8 s quiet period that groups a client's messages, and «بيكتب…» even while the chat was with the office (nobody answers then).
-- n8n now marks a message read as it arrives, with «بيكتب…» only when this is true. n8n (office_app) never reads hazem: this
-- function answers it a yes or a no, nothing else.

-- migrate:up
CREATE FUNCTION app.wa_hazem_answers(p_wa_id text) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $$
  SELECT COALESCE((SELECT s.value FROM hazem.settings s WHERE s.key = 'responder'), '') = 'hazem'
     AND (COALESCE((SELECT s.value FROM hazem.settings s WHERE s.key = 'hazem.open_to_all'), 'false') = 'true'
          OR p_wa_id = ANY (string_to_array(replace(COALESCE((SELECT s.value FROM hazem.settings s WHERE s.key = 'hazem.recipients'), ''), ' ', ''), ',')))
     AND COALESCE((SELECT c.state FROM hazem.conversations c JOIN hazem.persons p ON p.id = c.person_id WHERE p.wa_id = p_wa_id), 'bot_active') = 'bot_active'
$$;
REVOKE ALL ON FUNCTION app.wa_hazem_answers(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION app.wa_hazem_answers(text) TO office_app;

-- migrate:down
DROP FUNCTION app.wa_hazem_answers(text);
