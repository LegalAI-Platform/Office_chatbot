-- 0030_name_any_length: DECISIONS v1.29 (B-01, B-02). The owner (2026-10-28): «ممكن اسم حضرتك بس، مش شرط رباعي… ثنائي
-- رباعي براحته». Hazem asks for the name without «رباعي» and takes any name the client gives.

-- migrate:up
UPDATE hazem.approved_texts SET active = false WHERE key IN ('welcome_ask_name', 'ask_name_again') AND active;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('welcome_ask_name', 2, 'أهلًا بحضرتك في مكتب الأستاذ سعد فتحي سعد للمحاماة، معاك حازم. ممكن اسم حضرتك؟', 300, true,
   'DECISIONS v1.29 B-01', 'owner approval 2026-10-28'),
  ('ask_name_again', 2, 'معلش، ممكن اسم حضرتك؟', 300, true, 'DECISIONS v1.29 B-02', 'owner approval 2026-10-28')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
UPDATE hazem.approved_texts SET active = false WHERE source LIKE 'DECISIONS v1.29%';
DELETE FROM hazem.approved_texts t WHERE t.source LIKE 'DECISIONS v1.29%'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = true WHERE key IN ('welcome_ask_name', 'ask_name_again') AND version = 1
  AND NOT EXISTS (SELECT 1 FROM hazem.approved_texts a WHERE a.key = approved_texts.key AND a.active);
