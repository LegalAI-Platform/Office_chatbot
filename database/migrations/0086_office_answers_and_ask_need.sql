-- migrate:up
-- DECISIONS v1.115 (the owner, 2026-11-14, on the plan: «تمام نفّذ»).
--   office_answers_later: a reply that promises a person (human_request, handoff_waiting, service_detail…), when the office does
--   not answer within the hour (B-06 with the panel's office hours, booking.office_*), is followed by when it answers. The owner
--   of a request at 21:52: «هو حد اصلا من المكتب هيرد عليه دلوقتي؟ المفروض يقول له بكره… على حسب المواعيد». {{opens}} and
--   {{closes}}: the office's hours as said («10 الصبح»، «8 بالليل»); {{day}}: «بكرة» or «النهارده» (B-07). Word for word.
--   ask_need: asked to tell his matter, the client told something whose need the AI cannot place (a medical report in the
--   chat that led to this): what he needs the office to do about it, with the services and a person, never «احكيلي الموضوع»
--   again and never «معلش لو مش واضح» at once. The writer may say it in his words.

INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('office_answers_later', 1, 'المكتب بيرد من {{opens}} لـ {{closes}}، فحد من المكتب هيرد على حضرتك هنا {{day}} من الساعة {{opens}} 🙏', 300, true,
   'DECISIONS v1.115 B-06', 'owner: «تمام نفّذ» 2026-11-14'),
  ('ask_need', 1, 'وصلني اللي بعته 👍 محتاج المكتب يساعد حضرتك في إيه بخصوصه؟', 300, true,
   'DECISIONS v1.115 B-31', 'owner: «تمام نفّذ» 2026-11-14')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.source IN ('DECISIONS v1.115 B-06', 'DECISIONS v1.115 B-31')
   AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE source IN ('DECISIONS v1.115 B-06', 'DECISIONS v1.115 B-31');
