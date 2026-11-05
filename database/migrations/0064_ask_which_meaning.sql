-- 0064_ask_which_meaning: DECISIONS v1.76. «عايز اتجوز عرفي وعايز اعرف فيه امان؟» was sent to the office as a question. The owner:
-- «اسأله… قصدك ايه بامان… كلمة واحدة مش هتقدر تحدد»، «تمام اعملها». A question that can plainly mean two or three things (the AI
-- lists them, classify-v23) gets this question back, once; the writer words it with the chat, this text is the fallback.
-- {{meanings}} is what he may mean, joined with «، ولا » («إن العقد يبقى قانوني ومضمون، ولا إن الموضوع يفضل سري»).

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('ask_which_meaning', 1, 'حضرتك قصدك {{meanings}}؟ 🙏', 300, true, 'DECISIONS v1.76', 'owner approval 2026-11-06 (v1.76)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-06 (v1.76)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-06 (v1.76)';
