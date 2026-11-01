-- migrate:up
-- DECISIONS v1.45 (TASK 18, the owner 2026-11-01: «موافق»): after the transfer's photo the appointment waits for the office's
-- review: «محجوز لحضرتك، والمكتب بيراجع التحويل وهبعتلك التأكيد», never «متأكد ✅» before it. And the line «لو حابب تكتب
-- الموضوع…» is its own text, said only to a client who has not told his matter yet, and once: the situation texts lose it.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('situation_review', 1, 'أهلًا{{call}} 👋 ميعاد حضرتك ({{service}}) {{day}} الساعة {{time}} محجوز لحضرتك، والمكتب بيراجع التحويل وهبعتلك التأكيد.',
   300, true, 'DECISIONS v1.45 B-28', 'owner approval 2026-11-01'),
  ('situation_ask_topic', 1, 'لو حابب تكتب الموضوع باختصار قبل الميعاد، اكتبه هنا ويوصل للمستشار.',
   300, true, 'DECISIONS v1.45 B-28', 'owner approval 2026-11-01');

-- The two situation texts without that line: a new version only where the office has not reworded them since (as 0018).
DO $$
DECLARE
  r record;
  v_old hazem.approved_texts;
BEGIN
  FOR r IN SELECT * FROM (VALUES
    ('situation_confirmed', 'أهلًا{{call}} 👋 ميعاد حضرتك ({{service}}) {{day}} الساعة {{time}} متأكد ✅. لو حابب تكتب الموضوع باختصار قبل الميعاد، اكتبه هنا.',
                            'أهلًا{{call}} 👋 ميعاد حضرتك ({{service}}) {{day}} الساعة {{time}} متأكد ✅.'),
    ('situation_urgent', 'أكيد{{call}} 👋 حضرتك عندك استشارة عاجلة مدفوعة، والمستشار هيكلم حضرتك {{when}}. لو حابب تكتب الموضوع باختصار، اكتبه هنا ويوصل للمستشار.',
                         'أكيد{{call}} 👋 حضرتك عندك استشارة عاجلة مدفوعة، والمستشار هيكلم حضرتك {{when}}.')
  ) AS t(key, old_text, new_text)
  LOOP
    SELECT * INTO v_old FROM hazem.approved_texts WHERE key = r.key ORDER BY active DESC, version DESC LIMIT 1;
    CONTINUE WHEN NOT FOUND OR v_old.text <> r.old_text;
    UPDATE hazem.approved_texts SET active = false WHERE key = r.key AND active;
    INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
    VALUES (r.key, (SELECT max(version) + 1 FROM hazem.approved_texts WHERE key = r.key), r.new_text, v_old.max_chars, v_old.active,
            v_old.source, 'migration 0046');
  END LOOP;
END $$;

-- migrate:down
DELETE FROM hazem.approved_texts WHERE key IN ('situation_review', 'situation_ask_topic');
DO $$
DECLARE
  k text;
BEGIN
  FOREACH k IN ARRAY ARRAY['situation_confirmed', 'situation_urgent'] LOOP
    IF EXISTS (SELECT 1 FROM hazem.approved_texts WHERE key = k AND updated_by = 'migration 0046') THEN
      DELETE FROM hazem.approved_texts WHERE key = k AND updated_by = 'migration 0046';
      UPDATE hazem.approved_texts SET active = true WHERE key = k AND version = (SELECT max(version) FROM hazem.approved_texts WHERE key = k);
    END IF;
  END LOOP;
END $$;
