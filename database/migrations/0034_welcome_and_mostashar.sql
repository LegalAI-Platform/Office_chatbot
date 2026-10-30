-- 0034_welcome_and_mostashar: DECISIONS v1.32 (B-01.1, B-02, B-27). The owner (2026-10-30), his own words: the welcome
-- «أنا حازم، المساعد الرقمي لمكتب المستشار سعد فتحي سعد للمحاماة…» with two buttons instead of the name question; the
-- name asked at the booking («موافق علي اللي انت اقترحته»); «سعد فتحي سعد ده مستشار مش استاذ»: every active text that
-- says «الأستاذ» gets a new version with «المستشار» (and «المساعد الآلي» is «المساعد الرقمي», as in the welcome).

-- migrate:up
UPDATE hazem.approved_texts SET active = false WHERE key IN ('welcome_ask_name', 'services_menu') AND active;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('welcome', 1, 'أنا حازم، المساعد الرقمي لمكتب المستشار سعد فتحي سعد للمحاماة.
حضرتك تحب تعرف خدمات المكتب، ولا تحكيلي محتاج إيه وأساعدك؟', 300, true, 'DECISIONS v1.32 B-01', 'owner approval 2026-10-30 (v1.32)'),
  ('welcome_choose', 1, 'حضرتك تحب تعرف خدمات المكتب، ولا تحكيلي محتاج إيه وأساعدك؟', 300, true, 'DECISIONS v1.32 B-01',
   'owner approval 2026-10-30 (v1.32)'),
  ('ask_name_booking', 1, 'تمام، ممكن اسم حضرتك عشان أسجّل الميعاد؟', 300, true, 'DECISIONS v1.32 B-02', 'owner approval 2026-10-30 (v1.32)'),
  ('services_menu', 2, 'تحت أمرك يا فندم، دي خدمات المكتب وأسعارها. اختار اللي يناسب حضرتك.', 300, true, 'DECISIONS v1.32 B-01',
   'owner approval 2026-10-30 (v1.32)')
ON CONFLICT (key, version) DO NOTHING;

-- «الأستاذ» → «المستشار»: a new version of each active text (the old one stays for the replies that used it). A text
-- not from DECISIONS keeps its source; every row of 1.32 is marked by updated_by, for the way back.
CREATE TEMP TABLE mostashar ON COMMIT DROP AS
SELECT key, version, text, max_chars, source FROM hazem.approved_texts
 WHERE active AND (text LIKE '%الأستاذ%' OR text LIKE '%المساعد الآلي%');
UPDATE hazem.approved_texts a SET active = false FROM mostashar m WHERE a.key = m.key AND a.version = m.version;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
SELECT m.key, (SELECT max(v.version) + 1 FROM hazem.approved_texts v WHERE v.key = m.key),
       replace(replace(m.text, 'الأستاذ', 'المستشار'), 'المساعد الآلي', 'المساعد الرقمي'),
       m.max_chars, -- one letter longer: every renamed text is far under its limit (300 or 600)
       true, CASE WHEN m.source LIKE 'DECISIONS%' THEN 'DECISIONS v1.32 B-27' ELSE m.source END, 'owner approval 2026-10-30 (v1.32)'
  FROM mostashar m;

-- migrate:down
CREATE TEMP TABLE back_keys ON COMMIT DROP AS
SELECT DISTINCT key FROM hazem.approved_texts WHERE updated_by = 'owner approval 2026-10-30 (v1.32)' AND key NOT IN ('welcome', 'welcome_choose', 'ask_name_booking')
UNION SELECT 'welcome_ask_name';
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-10-30 (v1.32)';
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-10-30 (v1.32)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
-- each of those keys gets its newest version before 1.32 back
UPDATE hazem.approved_texts t SET active = true
  FROM back_keys b
 WHERE t.key = b.key
   AND t.version = (SELECT max(v.version) FROM hazem.approved_texts v WHERE v.key = t.key AND v.updated_by IS DISTINCT FROM 'owner approval 2026-10-30 (v1.32)');
