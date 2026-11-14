-- migrate:up
-- DECISIONS v1.111 (from Ahmed Amin's chat of 2026-11-14 04:23: «✅ ابدأ الطلب» under the drafting text got that very text
-- again, and nothing said the request had started; the owner: «General fix»). Pressed under a written service's text (or with
-- its order open already), the client is told the request is open and what to send. {{service}}: «صياغة العقد»، «مراجعة
-- العقد»، «الإنذار الرسمي».

INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('service_order_opened', 1, 'تمام، طلب {{service}} اتفتح ✅ ابعتلي هنا أي تفاصيل أو ورق عندك، والمستشار هيحدد السعر النهائي قبل أي دفع.', 300, true,
   'DECISIONS v1.111 B-16', 'owner: «General fix» 2026-11-14')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.source = 'DECISIONS v1.111 B-16'
   AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE source = 'DECISIONS v1.111 B-16';
