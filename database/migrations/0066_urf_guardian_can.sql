-- 0066_urf_guardian_can: DECISIONS v1.82. Ahmed Amin's chat (2026-11-06): «ينفع ابوها بدل اخوها؟» got the closing refusal, which
-- itself says the father may come. A question whether one of her family can be the guardian (the AI reads it, classify-v28
-- urf_guardian_asked) is answered and asked back before the case takes him: «أيوه» is that guardian, «لا» brings the guardian
-- question back. The owner: «موافق». {{guardian}} is filled by the code: الأب، الأخ or العم.

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_guardian_can', 1, 'أيوه، {{guardian}} ينفع يبقى الولي. هو هيحضر معاكم وموافق؟', 300, true, 'DECISIONS v1.82', 'owner approval 2026-11-06 (v1.82)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-06 (v1.82)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-06 (v1.82)';
