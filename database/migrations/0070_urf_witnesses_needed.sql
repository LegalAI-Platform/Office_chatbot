-- 0070_urf_witnesses_needed: DECISIONS v1.90. «طب هو لازم يكون في شهود؟ ولا ممكن نعمل العقد بينا إحنا الاتنين بس؟» was asked three
-- times in Ahmed Amin's chat (2026-11-07, hazem.urf_case_said on) and never answered. Asking whether witnesses are needed is asking
-- about going without them (classify-v33: witnesses asked none): the office's rule is said as an answer (never a legal opinion),
-- then the question waiting. The owner, 2026-11-07: «نفذ» on TASK 21's plan.

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_witnesses_needed', 1, 'أيوه، العقد عندنا لازم يكون فيه شاهدين: المكتب يوفّرهم، أو حضرتك تجيبهم.', 300, true, 'DECISIONS v1.90', 'owner approval 2026-11-07 (v1.90)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-07 (v1.90)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-07 (v1.90)';
