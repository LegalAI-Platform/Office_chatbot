-- 0063_urf_answer_corrected: DECISIONS v1.75. After v1.74 a corrected Urfi answer («لا قصدي الزوجه 25») was taken, but the
-- reply only asked the question waiting with «علشان نكمّل…», so he could think it was not understood. The owner: «خلي الرد يكون معبر
-- ويشمل كل الكيسزز». {{changed}} is what he corrected, filled by the code for every answer: the ages («السن: الزوج 26 سنة والزوجة
-- 25 سنة»), her past («إنها اتطلقت أو اختلعت قبل كده»، «إنها أول جوازة ليها»، «إنها أرملة»), the guardian («الولي يبقى الأخ») or
-- the witnesses («المكتب هيوفّر الشهود»، «الشهود هتجيبهم حضرتك»), several joined with «، و». Then the next question, or the
-- price and times again; a refusal says itself.

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_answer_corrected', 1, 'تمام، عدّلت {{changed}} ✅', 300, true, 'DECISIONS v1.75', 'owner approval 2026-11-06 (v1.75)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-06 (v1.75)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-06 (v1.75)';
