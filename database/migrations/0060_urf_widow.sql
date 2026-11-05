-- 0060_urf_widow: DECISIONS v1.68. The owner (2026-11-05): the divorce and the khul are one answer, «أرملة» is a new one, and the
-- guardian is asked (and comes, and his card is asked) only for a first marriage: «دي الحالة الوحيدة اللي انت محتاج فيها يكون
-- معاك مين اهل الزوجة موجودين وموافق». The question and the buttons as the owner approved them («اه اعمل كدا»). The cases
-- answered «اختلعت» before stay as they are.

-- migrate:up
ALTER TABLE hazem.urf_cases DROP CONSTRAINT urf_cases_wife_past_check;
ALTER TABLE hazem.urf_cases ADD CONSTRAINT urf_cases_wife_past_check CHECK (wife_past IN ('first', 'divorced', 'khul', 'widow'));

UPDATE hazem.approved_texts SET active = false WHERE key IN ('urf_ask_wife_past', 'btn_urf_divorced') AND version = 1;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_ask_wife_past', 2, 'دي أول جوازة للزوجة، ولا سبق لها الجواز (اتطلقت أو اختلعت أو أرملة)؟', 300, true, 'DECISIONS v1.68', 'owner approval 2026-11-05 (v1.68)'),
  ('btn_urf_divorced', 2, 'اتطلقت أو اختلعت', 20, true, 'DECISIONS v1.68', 'owner approval 2026-11-05 (v1.68)'),
  ('btn_urf_widow', 1, 'أرملة', 20, true, 'DECISIONS v1.68', 'owner approval 2026-11-05 (v1.68)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-05 (v1.68)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-05 (v1.68)';
UPDATE hazem.approved_texts SET active = true WHERE key IN ('urf_ask_wife_past', 'btn_urf_divorced') AND version = 1;
UPDATE hazem.urf_cases SET wife_past = 'divorced' WHERE wife_past = 'widow';
ALTER TABLE hazem.urf_cases DROP CONSTRAINT urf_cases_wife_past_check;
ALTER TABLE hazem.urf_cases ADD CONSTRAINT urf_cases_wife_past_check CHECK (wife_past IN ('first', 'divorced', 'khul'));
