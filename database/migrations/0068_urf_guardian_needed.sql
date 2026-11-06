-- 0068_urf_guardian_needed: DECISIONS v1.86. «هو لازم حد يجي معاها من اهلها؟» while his Urfi case is open is a question about
-- going without a guardian (classify-v31: guardian asked none). It is answered with the rule, then the question waiting comes
-- again; never the closing «نعتذر لحضرتك… لو محتاج أي خدمة تانية» (urf_guardian_closed), which stays for a refused case.
-- The owner, 2026-11-06: «موافق».

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_guardian_needed', 1, 'أيوه، لو دي أول جوازة للزوجة لازم يكون معاها حد من أهلها موافق: الأب أو الأخ أو العم.', 300, true, 'DECISIONS v1.86', 'owner approval 2026-11-06 (v1.86)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-06 (v1.86)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-06 (v1.86)';
