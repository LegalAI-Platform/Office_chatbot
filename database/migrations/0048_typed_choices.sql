-- 0048_typed_choices: DECISIONS v1.47 (B-32). The owner (2026-11-02), after Ahmed Amin's test chat («تمام بكره 10 وربع الصبح»
-- under the list of times got the consultation offer again): «اي الحل العام» and, on the plan, «اه». Words typed while
-- buttons or list rows wait are read as the choice they pick; a later booking step never falls back to the offer: it comes
-- back with booking_step_again, the next time the office takes it.

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('booking_step_again', 1, 'علشان نكمّل الحجز، اختار من اللي تحت 👇 أو اكتبلي اليوم والساعة اللي تناسب حضرتك.', 300, true,
   'DECISIONS v1.47', 'owner approval 2026-11-02 (v1.47)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-02 (v1.47)';
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-02 (v1.47)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
