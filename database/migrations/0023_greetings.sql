-- 0023_greetings: DECISIONS v1.21 (B-01.6). The client's greeting is answered in kind, read by code (Courtesy, never
-- the AI), as a short message before Hazem's answer: «السلام عليكم» → «وعليكم السلام ورحمة الله وبركاته», «صباح الخير»
-- → «صباح النور», and so on. A goodbye alone (past the first message) is answered alone. The owner asked on 2026-10-27
-- («تهندل كل الحالات بتاعت التحيه») after the proposal in the chat; approved, so active at once. Each one can be
-- changed from the panel («نصوص حازم») like any approved text.

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('greet_salam',       1, 'وعليكم السلام ورحمة الله وبركاته', 300, true, 'DECISIONS v1.21 B-01.6', 'migration 0023'),
  ('greet_ramadan',     1, 'الله أكرم',                        300, true, 'DECISIONS v1.21 B-01.6', 'migration 0023'),
  ('greet_jumua',       1, 'علينا وعليكم',                     300, true, 'DECISIONS v1.21 B-01.6', 'migration 0023'),
  ('greet_occasion',    1, 'وحضرتك بالصحة والسلامة',           300, true, 'DECISIONS v1.21 B-01.6', 'migration 0023'),
  ('greet_morning',     1, 'صباح النور',                       300, true, 'DECISIONS v1.21 B-01.6', 'migration 0023'),
  ('greet_evening',     1, 'مساء النور',                       300, true, 'DECISIONS v1.21 B-01.6', 'migration 0023'),
  ('greet_how_are_you', 1, 'الحمد لله، بخير',                  300, true, 'DECISIONS v1.21 B-01.6', 'migration 0023'),
  ('greet_good_night',  1, 'وحضرتك من أهله',                   300, true, 'DECISIONS v1.21 B-01.6', 'migration 0023'),
  ('greet_goodbye',     1, 'مع السلامة، في رعاية الله',        300, true, 'DECISIONS v1.21 B-01.6', 'migration 0023')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts WHERE key LIKE 'greet\_%';
