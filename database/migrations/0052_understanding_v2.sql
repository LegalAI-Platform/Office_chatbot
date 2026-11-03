-- 0052_understanding_v2: DECISIONS v1.52 (phase 1). The owner (2026-11-04), on the general solution: «تمام وابدا نفذ»;
-- on a client who finds it too dear: the fee counts towards a case's fees («ايوه») and no task for it («دا بيسال عادي»).
-- The switch hazem.understanding_v2 (off: Hazem exactly as before), and the two texts it answers with.

-- migrate:up
INSERT INTO hazem.settings (key, value, description, updated_by) VALUES
  ('hazem.understanding_v2', 'false', 'v1.52 (phase 1): Hazem reads the whole chat and the client''s stance (true), or exactly as before (false)',
   'migration 0052')
ON CONFLICT (key) DO NOTHING;

INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('no_pressure', 1, 'تحت أمرك يا فندم 🙏 ولو حبيت تكمّل في أي وقت، اكتبلي هنا وأنا معاك.', 300, true, 'DECISIONS v1.52',
   'owner approval 2026-11-04 (v1.52)'),
  ('price_objection', 1, 'فاهم حضرتك تمامًا 🙏 الاستشارة مع المستشار سعد فتحي سعد نفسه، ولو الموضوع وصل لقضية تمنها بيتخصم من الأتعاب. ولو المقابلة في المكتب كتير، فيه استشارة بالتليفون أو الفيديو بـ{{price_phone}} جنيه ربع ساعة. ولو مش مناسب دلوقتي، أنا موجود في أي وقت.',
   600, true, 'DECISIONS v1.52', 'owner approval 2026-11-04 (v1.52)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-04 (v1.52)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-04 (v1.52)';
DELETE FROM hazem.settings WHERE key = 'hazem.understanding_v2';
