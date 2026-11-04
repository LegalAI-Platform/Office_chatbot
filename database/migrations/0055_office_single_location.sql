-- 0055_office_single_location: DECISIONS v1.58. The owner (2026-11-04), on «عندكم فرع في اسكندرية؟» (Hazem could only send the
-- address, and the reviewer rightly refused «مفيش فرع في الإسكندرية»: the facts did not say it): «عاوزك اكيد تزود دي». «حقايق
-- المكتب» (office_facts_other) gets a new version with the line; the latest version is kept as it is (the office may have
-- edited it in «النصوص») and the line is added to its end. Two statements: the unique «one active per key» index is checked
-- per statement, so the old version is switched off first.

-- migrate:up
UPDATE hazem.approved_texts SET active = false WHERE key = 'office_facts_other' AND active;
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
SELECT 'office_facts_other', cur.version + 1, cur.text || E'
للمكتب مقر واحد بس ومفيش فروع تانية، وبيقبل قضايا من كل المحافظات.', 600, true,
       'proposal TASK 18', 'owner approval 2026-11-04 (v1.58)'
  FROM (SELECT version, text FROM hazem.approved_texts WHERE key = 'office_facts_other' ORDER BY version DESC LIMIT 1) cur;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-04 (v1.58)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-04 (v1.58)';
UPDATE hazem.approved_texts t SET active = true
 WHERE t.key = 'office_facts_other'
   AND t.version = (SELECT max(v.version) FROM hazem.approved_texts v
                     WHERE v.key = 'office_facts_other' AND v.updated_by IS DISTINCT FROM 'owner approval 2026-11-04 (v1.58)');
