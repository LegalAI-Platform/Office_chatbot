-- 0069_urf_guardian_out: DECISIONS v1.87. «اخو العروسه مش هيجي» brought the guardian question back with «الأخ» in it. The guardians
-- he says will not come (or do not agree) are kept with the case, and the guardian question is asked again without them, in the
-- owner's approved words (urf_ask_guardian_other, {{options}} filled by the code: «الأب، ولا العم»). The owner, 2026-11-06: «تمام».

-- migrate:up
ALTER TABLE hazem.urf_cases ADD COLUMN guardian_out text
  CHECK (guardian_out IS NULL OR guardian_out ~ '^(father|brother|uncle)(,(father|brother|uncle)){0,2}$');
COMMENT ON COLUMN hazem.urf_cases.guardian_out IS 'v1.87: the guardians he said will not come, comma-separated (father, brother, uncle)';

INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_ask_guardian_other', 1, 'طيب، مين تاني من أهل الزوجة موجود وموافق: {{options}}؟', 300, true, 'DECISIONS v1.87', 'owner approval 2026-11-06 (v1.87)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-06 (v1.87)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-06 (v1.87)';
ALTER TABLE hazem.urf_cases DROP COLUMN guardian_out;
