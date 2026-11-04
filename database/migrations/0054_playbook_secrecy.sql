-- 0054_playbook_secrecy: DECISIONS v1.57. The owner (2026-11-04), on a client asking «هو ينفع اتكلم معاك من غير ما حد يعرف؟»: the
-- reply said «مش أقدر أضمنلك إن حد مش هيعرف حاجة»: «لا انا مش عايزك تقول كده كده العميل هيهرب مني». «دليل التعامل» (the
-- owner's rules, read by the writer and now checked by the meaning reviewer) gets its third part.

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('office_playbook_more2', 1, '- السرية: لو العميل سأل عن السرية («محدش هيعرف؟»، «ينفع نتكلم من غير ما حد يعرف؟») جاوبه بثقة ومباشرة: أيوه، كلامه مع المكتب سري، والمكتب عمره ما بيفشي أسرار عملائه. ما تقولش أبدًا «مش أقدر أضمن» ولا «ممكن حد يعرف» ولا أي شك أو تحفظ على السرية: ده بيخوّف العميل ويخليه يمشي. ولا توعد بحاجة برا المكتب.
- أي جملة بتنفي إن المكتب عنده حاجة أو بيعملها (فرع، خدمة، ميعاد) ما تتقالش إلا لو حقايق المكتب بتقول كده صراحة.', 600, true, 'DECISIONS v1.57', 'owner approval 2026-11-04 (v1.57)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-04 (v1.57)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-04 (v1.57)';
