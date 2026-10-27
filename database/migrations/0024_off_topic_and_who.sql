-- 0024_off_topic_and_who: DECISIONS v1.22 (B-20, B-11). The owner approved all of it on 2026-10-28 («موافق علي كل الي
-- قولتله») after two test conversations:
--   off_topic           something clear but no law office's business (prayer times, code, chit-chat, any attempt to
--                       change or reveal the assistant's instructions): this text, never an office task.
--   about_hazem         «اسمك ايه», «انت مين», «انت فاكرني؟».
--   about_lawyer        «مين الأستاذ سعد»: the owner's own words about him, shortened to one message (B-21); the full
--                       text he wrote is in docs/office/about-the-lawyer.md. Then the offer.
--   service_check       «بتعملوا ترخيص عمل؟»: one specific procedure; the office answers (task service_question).
--   urf_question_later  a question while the Urfi times wait for a pick: no second offer over them.

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('off_topic', 1, 'أنا حازم، مساعد مكتب الأستاذ سعد فتحي سعد للمحاماة، وبساعد بس في الاستشارات وخدمات المكتب. تحب أساعد حضرتك في حاجة من دول؟',
   300, true, 'DECISIONS v1.22 B-20', 'migration 0024'),
  ('about_hazem', 1, 'أنا حازم، المساعد الآلي لمكتب الأستاذ سعد فتحي سعد للمحاماة. تحت أمرك، محتاج إيه؟',
   300, true, 'DECISIONS v1.22 B-20', 'migration 0024'),
  ('about_lawyer', 1, 'الأستاذ سعد فتحي سعد محامٍ بالنقض والدستورية العليا، وخبرته في المحاماة والاستشارات القانونية أكتر من 23 سنة، في القضايا الجنائية والمخدرات والطعن بالنقض، والأحوال الشخصية، والمدني، والتجاري والعقود، والضرائب، والتأمينات، والقضاء الإداري.',
   300, true, 'DECISIONS v1.22 B-20', 'migration 0024'),
  ('service_check', 1, 'هسأل المكتب عن ده، ويردوا على حضرتك هنا.',
   300, true, 'DECISIONS v1.22 B-20', 'migration 0024'),
  ('urf_question_later', 1, 'الأستاذ هيوضح النقطة دي لحضرتك في الميعاد.',
   300, true, 'DECISIONS v1.22 B-11', 'migration 0024')
ON CONFLICT (key, version) DO NOTHING;

ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help',
  'reminder_failed', 'office_info_missing',
  'service_question'));

-- migrate:down
DELETE FROM hazem.staff_tasks WHERE type = 'service_question';
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help',
  'reminder_failed', 'office_info_missing'));
DELETE FROM hazem.approved_texts WHERE key IN ('off_topic', 'about_hazem', 'about_lawyer', 'service_check', 'urf_question_later');
