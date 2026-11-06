-- 0067_urf_case_answers: DECISIONS v1.84 (TASK 20). What the client says about his Urfi case is read as one list (said / asked /
-- withdrawn, classify-v30). A question about one answer («ينفع أجيب الشهود؟»، «لو مطلقة ينفع؟»، «ينفع خالها؟»، «لو هي 17 ينفع؟») is
-- answered by the case's own rules, never sent to the office, and never changes the case. The owner: «Approve, build it».
-- {{option}} is filled by the code in the field's own words («إن حضرتك تجيب الشهود»، «إن الزوجة تكون مطلقة أو مختلعة»);
-- {{who}} is his own word for the relative when it is in his message («خالها»), else «القريب ده».

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_option_can', 1, 'أيوه، {{option}} ينفع ✅', 300, true, 'DECISIONS v1.84', 'owner approval 2026-11-06 (v1.84)'),
  ('urf_guardian_cannot', 1, 'للأسف {{who}} مينفعش يبقى الولي. سياسة المكتب: لو دي أول جوازة للزوجة، لازم يكون معاها الأب أو الأخ أو العم.', 300, true, 'DECISIONS v1.84', 'owner approval 2026-11-06 (v1.84)'),
  ('urf_age_cannot', 1, 'للأسف لأ، سياسة المكتب إن الزوج والزوجة يكون سنهم 18 سنة أو أكتر.', 300, true, 'DECISIONS v1.84', 'owner approval 2026-11-06 (v1.84)')
ON CONFLICT (key, version) DO NOTHING;

-- The switch (the owner, 2026-11-06: «المفتاح… مقفول تبقى النسخه اللي كانت ع السيرفر… بتاعت محمد، مفتوح يبقى شغلنا»): off is
-- Hazem exactly as v1.81 in the Urfi case (classify-v27, its fields, 15 seconds); on is v1.82–v1.84 (classify-v30, case_said, 30 s).
INSERT INTO hazem.settings (key, value, description) VALUES
  ('hazem.urf_case_said', 'false', 'v1.84 (TASK 20): the Urfi case read as one list, said / asked / taken back (true), or exactly as v1.81 (false)')
ON CONFLICT (key) DO NOTHING;

-- migrate:down
DELETE FROM hazem.settings WHERE key = 'hazem.urf_case_said';
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-06 (v1.84)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-06 (v1.84)';
