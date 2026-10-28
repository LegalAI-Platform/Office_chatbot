-- 0031_services_menu: DECISIONS v1.30 (B-01.3). The owner (2026-10-28): after the welcome and the name, before the client
-- says his problem, show the office's services («القايمة»). A WhatsApp list: the two consultations (the booking's own
-- buttons), a case, a document review, a contract, a formal notice, a written opinion, an Urfi contract, and «احكي مشكلتي».

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('services_menu', 1, 'تحت أمرك. اختار الخدمة اللي محتاجها من القايمة 👇، أو احكيلي الموضوع باختصار.', 300, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('btn_services_list', 1, 'خدمات المكتب', 20, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('services_tell', 1, 'تحت أمرك، احكيلي الموضوع باختصار.', 300, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('row_consult_phone', 1, '📞 استشارة تليفون/فيديو', 24, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('row_consult_office', 1, '🏢 مقابلة في المكتب', 24, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('row_case', 1, '⚖️ رفع قضية', 24, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('row_review', 1, '📄 مراجعة عقد أو مستندات', 24, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('row_drafting', 1, '✍️ صياغة عقد', 24, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('row_notice', 1, '📨 إنذار رسمي', 24, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('row_opinion', 1, '📝 رأي قانوني مكتوب', 24, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('row_urf', 1, '💍 جواز أو طلاق عرفي', 24, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28'),
  ('row_tell', 1, '💬 أحكي مشكلتي', 24, true, 'DECISIONS v1.30 B-01', 'owner approval 2026-10-28')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
UPDATE hazem.approved_texts SET active = false WHERE source = 'DECISIONS v1.30 B-01';
DELETE FROM hazem.approved_texts t WHERE t.source = 'DECISIONS v1.30 B-01'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
