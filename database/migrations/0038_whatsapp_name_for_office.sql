-- 0038_whatsapp_name_for_office: DECISIONS v1.36 (the owner, 2026-10-31, «Yes, everywhere»).
--   A client who never gave his name was «من غير اسم» on the lawyer's calendar and nameless in the panel, while the urgent
--   alert already used his WhatsApp profile name. The office now sees that name everywhere, marked «(اسم الواتساب)» so it
--   is never taken for the name he gave. Hazem never says it to the client.
--   hazem.persons.whatsapp_name: the profile name WhatsApp sends with his messages (the latest, cleaned, 60 characters at
--   most), written with every turn. office_name: the name he gave, else that one marked, else null.

-- migrate:up
ALTER TABLE hazem.persons ADD COLUMN whatsapp_name text CHECK (char_length(whatsapp_name) BETWEEN 1 AND 60);
ALTER TABLE hazem.persons ADD COLUMN office_name text GENERATED ALWAYS AS
  (coalesce(nullif(btrim(name_raw), ''), whatsapp_name || ' (اسم الواتساب)')) STORED;

-- The name each client's latest message carried.
UPDATE hazem.persons p SET whatsapp_name = n.name
  FROM (SELECT DISTINCT ON (t.wa_id) t.wa_id, left(btrim(regexp_replace(t.contact_name, '[[:cntrl:]]', '', 'g')), 60) AS name
          FROM hazem.turns t WHERE t.contact_name IS NOT NULL ORDER BY t.wa_id, t.created_at DESC) n
 WHERE p.wa_id = n.wa_id AND n.name <> '';

-- migrate:down
ALTER TABLE hazem.persons DROP COLUMN IF EXISTS office_name;
ALTER TABLE hazem.persons DROP COLUMN IF EXISTS whatsapp_name;
