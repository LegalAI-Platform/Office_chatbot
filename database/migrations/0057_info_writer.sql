-- 0057_info_writer: DECISIONS v1.61. The owner's chat of 2026-11-04: «ليكم فرع في اسكندريه» got the address again, word for word.
-- The switch hazem.info_writer (off: Hazem exactly as before): the address and the hours stay the approved text, and the writer
-- adds ONE sentence before them that answers what he asked.

-- migrate:up
INSERT INTO hazem.settings (key, value, description, updated_by) VALUES
  ('hazem.info_writer', 'false', 'v1.61: a sentence that answers his question before the office''s address or hours (true), or the approved text alone (false)',
   'migration 0057')
ON CONFLICT (key) DO NOTHING;

-- migrate:down
DELETE FROM hazem.settings WHERE key = 'hazem.info_writer';
