-- migrate:up
-- TASK 16 (DECISIONS v1.39): Hazem answers the client's situation, not just his message.
-- 1. The gender of the name the client typed (the AI reads it, classify-v13), for «يا أستاذ محمد» / «يا أستاذة منى»;
--    null = not clear (Hazem then says «حضرتك» without a name). Never from the WhatsApp name.
-- 2. The situation replies, the forbidden rule 2's text and its button, and the other wordings (3.6), approved by the
--    owner on 2026-10-31. {{call}} is « يا أستاذ محمد», « يا أبو عماد» or nothing (the sentence reads without it).

ALTER TABLE hazem.persons ADD COLUMN name_gender text CHECK (name_gender IN ('male', 'female'));

INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('situation_urgent', 1, 'أكيد{{call}} 👋 حضرتك عندك استشارة عاجلة مدفوعة، والمستشار هيكلم حضرتك {{when}}. لو حابب تكتب الموضوع باختصار، اكتبه هنا ويوصل للمستشار.',
   300, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31'),
  ('situation_held', 1, 'أهلًا{{call}} 👋 ميعاد حضرتك ({{service}}) {{day}} الساعة {{time}} ممسوك لحد الساعة {{deadline}}، ومستني صورة التحويل بس.',
   300, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31'),
  ('situation_confirmed', 1, 'أهلًا{{call}} 👋 ميعاد حضرتك ({{service}}) {{day}} الساعة {{time}} متأكد ✅. لو حابب تكتب الموضوع باختصار قبل الميعاد، اكتبه هنا.',
   300, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31'),
  ('situation_order', 1, 'أهلًا{{call}} 👋 طلب حضرتك ({{service}}) {{status}}. لو عندك سؤال عنه اكتبه هنا.',
   300, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31'),
  ('situation_topic', 1, 'تمام{{call}}، وصلت ✅ وهتبقى قدام المستشار.', 300, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31'),
  ('repeat_choice', 1, 'معلش لو مش واضح، تحب تشوف خدمات المكتب، ولا تكلم حد من المكتب؟', 300, true, 'DECISIONS v1.39 TASK-16',
   'owner approval 2026-10-31'),
  ('btn_talk_to_office', 1, 'أكلم حد من المكتب', 20, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31'),
  ('greeting_2', 1, 'اتفضل{{call}}، أقدر أساعد حضرتك في إيه؟', 300, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31'),
  ('greeting_3', 1, 'أنا معاك{{call}}، قولّي محتاج إيه.', 300, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31'),
  ('topic_received_2', 1, 'وصلت يا فندم ✅', 300, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31'),
  ('topic_received_3', 1, 'تمام{{call}}، وصلت ✅', 300, true, 'DECISIONS v1.39 TASK-16', 'owner approval 2026-10-31')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.source = 'DECISIONS v1.39 TASK-16'
   AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE source = 'DECISIONS v1.39 TASK-16';
ALTER TABLE hazem.persons DROP COLUMN name_gender;
