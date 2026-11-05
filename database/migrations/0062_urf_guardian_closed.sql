-- 0062_urf_guardian_closed: DECISIONS v1.71. Ahmed Amin's chat (2026-11-05): after «مفيش حد منهم» for a first marriage got
-- urf_no_guardian, «ليه كده» opened a new Urfi case from its first question (the ages, again). The guardian refusal now closes the
-- same matter like the age refusal does (v1.20, B-11.1): this text twice at most for 24 hours, then silence. The owner: «اقترح نص
-- واعمله شوف الصح واعمله». It says why, as the question «ليه» asks, with the office's policy only (never «the law»).

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_guardian_closed', 1, 'نعتذر لحضرتك، دي سياسة المكتب: لو دي أول جوازة للزوجة، لازم يكون معاها حد من أهلها موافق (الأب أو الأخ أو العم). لو محتاج حضرتك أي خدمة تانية، أنا موجود.', 300, true, 'DECISIONS v1.71', 'owner approval 2026-11-05 (v1.71)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-05 (v1.71)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-05 (v1.71)';
