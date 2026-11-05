-- 0059_time_not_booked: DECISIONS v1.67. The owner (2026-11-05), on «انهارده الساعة 4» typed at 4:41 that got the list of times with no
-- word why: «موافق» on the two texts that say it before the free times: the time is already past today, or that exact time is not
-- free. {{time}} is the time he asked, as the lists show it («4:00 م»). Without them the list goes alone, as in 1.66.

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('booking_time_passed', 1, 'الساعة {{time}} عدّت النهارده 🙏 دي أقرب المواعيد الفاضية 👇', 300, true, 'DECISIONS v1.67', 'owner approval 2026-11-05 (v1.67)'),
  ('booking_time_taken', 1, 'الساعة {{time}} مش فاضية للأسف 🙏 دي أقرب المواعيد الفاضية 👇', 300, true, 'DECISIONS v1.67', 'owner approval 2026-11-05 (v1.67)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-05 (v1.67)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-05 (v1.67)';
