-- migrate:up
-- DECISIONS v1.113 (the owner, 2026-11-14: «انا عايز الاستشارات تبقى لغايه 12 بالليل عادي. المقابلات في المكتب من 10 الصبح ل 8
-- بالليل» … «تمام نفّذ»).
--   The panel's week stays the consultations' hours: every day 10:00–24:00 on the test server, for the phone, video and urgent
--   calls. The office's meetings and the Urfi signings now keep inside these minutes of the day as well, within the week and
--   its exceptions: from 10:00, ending by 20:00 (the last half-hour meeting starts at 19:30, the last hour-long signing at 19:00).
--   «إعدادات الحجز» in the panel changes them; both empty = the panel's hours for the office too. A time already booked stays.
INSERT INTO hazem.settings (key, value, description) VALUES
  ('booking.office_start_minute', '600',
   'v1.113: the office''s meetings and Urfi signings start from this minute of the day (600 = 10:00), inside the panel''s hours'),
  ('booking.office_end_minute', '1200',
   'v1.113: the office''s meetings and Urfi signings end by this minute of the day (1200 = 20:00); both empty = the panel''s hours')
ON CONFLICT (key) DO NOTHING;

-- migrate:down
DELETE FROM hazem.settings WHERE key IN ('booking.office_start_minute', 'booking.office_end_minute');
