-- migrate:up
-- DECISIONS v1.42 (the urgent call at night, from Abu El-Leil's conversation, 2026-11-01): the receipt of an urgent call that
-- the lawyer takes later (his hours are closed now, or it is another day) no longer gets booking_paid («أول ما المكتب
-- يراجع التحويل، هبعتلك التأكيد»: no confirmation follows an urgent call, the lawyer checks the transfer himself) but its own
-- text, approved by the owner on 2026-11-01 («اه»).

INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urgent_later', 1, 'وصلت صورة التحويل ✅. المستشار هيكلم حضرتك على الرقم ده {{day}} الساعة {{time}}.', 300, true,
   'DECISIONS v1.42 B-09', 'owner approval 2026-11-01')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.source = 'DECISIONS v1.42 B-09'
   AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE source = 'DECISIONS v1.42 B-09';
