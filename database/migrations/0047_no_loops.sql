-- 0047_no_loops: DECISIONS v1.46. The owner (2026-11-01), after the test chats «انا تعبان» → «مش واضحلي المطلوب» and
-- «لو هتجوز عرفي هل في خصوصية» → «سن الزوج كام…» twice: «انت شايف ان ده حل عملي ولا احنا كده بنحل الكيس دي بس؟», and
-- on the general fixes: «طب نفذها»; on the privacy question: «هو كان بيسال يعني العرفي في خصوصيه يعني المكتب مش بيفشي
-- اسرار العملاء» (Hazem answers it himself: office_privacy). Any other question about how a service goes reaches the office (service_detail, task
-- service_question); a Urfi question asked last time comes back with «علشان نكمّل…» (urf_step_again), then the office;
-- «مش واضح» is softer (unclear v2).

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('office_privacy', 1, 'أكيد يا فندم 🔒 كل كلام حضرتك مع المكتب سري تمامًا، والمكتب عمره ما بيفشي أسرار عملائه.', 300, true, 'DECISIONS v1.46', 'owner approval 2026-11-01 (v1.46)'),
  ('service_detail', 1, 'سؤال مهم 👌 هوصّله للمكتب، ويردوا على حضرتك هنا.', 300, true, 'DECISIONS v1.46', 'owner approval 2026-11-01 (v1.46)'),
  ('urf_step_again', 1, 'علشان نكمّل طلب حضرتك، محتاج إجابة السؤال ده 👇', 300, true, 'DECISIONS v1.46', 'owner approval 2026-11-01 (v1.46)')
ON CONFLICT (key, version) DO NOTHING;

UPDATE hazem.approved_texts SET active = false WHERE key = 'unclear' AND active;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
SELECT 'unclear', max(version) + 1, 'معلش، ممكن توضّحلي أكتر حضرتك محتاج إيه؟ وأنا معاك 🙏', 300, true, 'DECISIONS v1.46',
       'owner approval 2026-11-01 (v1.46)'
  FROM hazem.approved_texts WHERE key = 'unclear';

-- migrate:down
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-01 (v1.46)';
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-01 (v1.46)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts t SET active = true
 WHERE t.key = 'unclear'
   AND t.version = (SELECT max(v.version) FROM hazem.approved_texts v WHERE v.key = 'unclear'
                     AND v.updated_by IS DISTINCT FROM 'owner approval 2026-11-01 (v1.46)');
