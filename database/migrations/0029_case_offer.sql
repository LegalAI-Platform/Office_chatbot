-- 0029_case_offer: DECISIONS v1.28 (B-23). The owner (2026-10-28): a client who wants a case filed — said outright or
-- understood from his story — hears that the lawyer listens first and sets the steps and the fees, with the consultation
-- counting towards the fees when the case is agreed within 30 days. «القضية بكام؟»: the fees are set after the lawyer listens.
-- The fee policy answer says the same 30 days.

-- migrate:up
UPDATE hazem.approved_texts SET active = false WHERE key IN ('price_case_representation', 'office_fee_policy') AND active;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('case_offer', 1, 'تحت أمرك. الأستاذ سعد هيسمع موضوع حضرتك بنفسه الأول ويقولك الخطوات والأتعاب. متاح استشارة بالتليفون بـ{{price_phone}} أو مقابلة في المكتب بـ{{price_office}}، ولو اتفقت مع المكتب على القضية خلال 30 يوم المبلغ بيتخصم من الأتعاب. تحب أنهي واحدة؟',
   300, true, 'DECISIONS v1.28 B-23', 'owner approval 2026-10-28'),
  ('price_case_representation', 2, 'الأتعاب بيحددها الأستاذ بعد ما يسمع الموضوع.', 300, true, 'DECISIONS v1.28 B-23', 'owner approval 2026-10-28'),
  ('office_fee_policy', 2, 'مبلغ الاستشارة بيتخصم من أتعاب القضية لو اتفقت مع المكتب عليها خلال 30 يوم من الاستشارة.', 300, true,
   'DECISIONS v1.28 B-23', 'owner approval 2026-10-28')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
UPDATE hazem.approved_texts SET active = false WHERE source = 'DECISIONS v1.28 B-23';
DELETE FROM hazem.approved_texts t WHERE t.source = 'DECISIONS v1.28 B-23'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = true WHERE key IN ('price_case_representation', 'office_fee_policy') AND version = 1
  AND NOT EXISTS (SELECT 1 FROM hazem.approved_texts a WHERE a.key = approved_texts.key AND a.active);
