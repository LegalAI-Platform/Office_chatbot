-- 0071_welcome_no_assistant: DECISIONS v1.93. The owner (2026-11-07): Hazem never says by himself that he is a digital
-- assistant, and never claims to be a person; asked who or what he is, he says «أنا حازم، المساعد الرقمي…» (about_hazem,
-- unchanged). The welcome said it in the first message unasked: a new version without it («شيلها», on «أنا حازم من مكتب
-- المستشار سعد فتحي سعد للمحاماة»). The old version stays for the replies that used it.

-- migrate:up
UPDATE hazem.approved_texts SET active = false WHERE key = 'welcome' AND active;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
SELECT 'welcome', (SELECT COALESCE(max(v.version), 0) + 1 FROM hazem.approved_texts v WHERE v.key = 'welcome'),
       'أنا حازم من مكتب المستشار سعد فتحي سعد للمحاماة.
حضرتك تحب تعرف خدمات المكتب، ولا تحكيلي محتاج إيه وأساعدك؟', 300, true, 'DECISIONS v1.93 B-01', 'owner approval 2026-11-07 (v1.93)';

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-07 (v1.93)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-07 (v1.93)';
UPDATE hazem.approved_texts SET active = true WHERE key = 'welcome' AND version = 1;
