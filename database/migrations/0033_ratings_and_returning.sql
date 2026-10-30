-- 0033_ratings_and_returning: DECISIONS v1.31. The owner (2026-10-30, «شوف الصح واعمله» then «موافق»):
--   B-01.7  a returning client. A known person whose request is finished (its appointment completed, no-show,
--           cancelled or expired, its service order delivered or closed, its Urfi case refused; nothing else open)
--           or who has been silent intake.fresh_after_days (7) with nothing pending gets a new intake: the name is
--           kept, «أهلًا بحضرتك تاني يا …» and the services list. Nothing is deleted (the old intake is closed).
--   B-26    the service rating, once per finished service: 2 hours after a consultation (office or phone / video) or an
--           Urfi contract is marked completed, 24 hours after a written service is delivered. Never to a muted
--           conversation, an urgent consultation or an Urfi divorce, and not twice within 30 days to one person.
--   hazem.service_ratings  one row per finished service (an appointment or a service order), made and sent by the API's
--           worker like the reminders (0017): a WhatsApp list inside the 24-hour window, the M-03 template
--           service_rating outside it. Until Meta approves that template, a rating whose window closed is skipped
--           (no_template), never a task. The client's row (5, 4, 3, 2 or «ليس الآن») is read by code; «يحتاج للتحسين»
--           (2) is a task for the managers (rating_low, its own bell alert task_rating), and the client's next words
--           within 24 hours are its reason: the task's turn moves to them, so the task shows them as «كلام العميل».
--   appointments.completed_at  when the office marked the appointment completed (the rating's clock).
--   rating.since  only services finished after this migration get a rating (an old one is never asked about).

-- migrate:up
ALTER TABLE hazem.appointments ADD COLUMN completed_at timestamptz;

CREATE TABLE hazem.service_ratings (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  person_id        uuid NOT NULL REFERENCES hazem.persons (id) ON DELETE CASCADE,
  appointment_id   uuid UNIQUE REFERENCES hazem.appointments (id) ON DELETE CASCADE,
  service_order_id uuid UNIQUE REFERENCES hazem.service_orders (id) ON DELETE CASCADE,
  -- the appointment's service or the order's kind; never an urgent consultation or an Urfi divorce (B-26)
  service          text NOT NULL CHECK (service IN ('consultation_phone_video', 'consultation_office', 'urf_marriage_office',
                     'urf_marriage_own', 'document_review', 'contract_drafting', 'formal_notice', 'written_opinion')),
  finished_at      timestamptz NOT NULL,  -- completed (an appointment) or delivered (an order)
  due_at           timestamptz NOT NULL,
  -- sending: claimed by the worker right before it goes (a send cut off half way is tried again after 5 minutes,
  -- under the same idempotency key, so n8n never sends it twice).
  state            text NOT NULL DEFAULT 'scheduled' CHECK (state IN ('scheduled', 'sending', 'sent', 'failed', 'skipped')),
  -- off: rating.enabled is false or its texts are not approved; muted: the conversation is muted (S-01); recent: the
  -- person was asked within rating.repeat_days; late: the worker reached it more than a day after its time;
  -- no_template: outside the 24-hour window and the template service_rating is not approved (yet).
  skip_reason      text CHECK (skip_reason IN ('off', 'muted', 'recent', 'late', 'no_template')),
  channel          text CHECK (channel IN ('message', 'template')),
  error_code       text CHECK (length(error_code) <= 80),
  sent_at          timestamptz,
  score            smallint CHECK (score BETWEEN 2 AND 5), -- null: not answered, or «ليس الآن»
  answered_at      timestamptz,
  answered_turn_id text,                  -- the press: its task (rating_low) has this turn until the reason comes
  reason_at        timestamptz,           -- the client's words after «يحتاج للتحسين» were taken as its reason
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_at       timestamptz NOT NULL DEFAULT now(),
  CHECK (num_nonnulls(appointment_id, service_order_id) = 1),
  CHECK ((state = 'skipped') = (skip_reason IS NOT NULL)),
  CHECK (state <> 'sent' OR (channel IS NOT NULL AND sent_at IS NOT NULL)),
  CHECK (answered_at IS NULL OR state = 'sent'),
  CHECK (score IS NULL OR answered_at IS NOT NULL)
);
CREATE INDEX service_ratings_due_idx ON hazem.service_ratings (due_at) WHERE state = 'scheduled';
CREATE INDEX service_ratings_person_idx ON hazem.service_ratings (person_id, sent_at);
CREATE INDEX service_ratings_sent_idx ON hazem.service_ratings (sent_at) WHERE state = 'sent';
CREATE TRIGGER trg_service_ratings_updated_at BEFORE UPDATE ON hazem.service_ratings
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

INSERT INTO hazem.settings (key, value, description) VALUES
  ('intake.fresh_after_days', '7', 'B-01.7 (1.31): a known person silent this many days with nothing pending starts a new request (welcome back and the services list)'),
  ('rating.enabled', 'true', 'B-26 (1.31): false = no service rating is sent (the ones that come due are skipped «off»)'),
  ('rating.after_appointment_hours', '2', 'B-26 (1.31): the rating goes this many hours after a consultation or an Urfi contract is marked completed'),
  ('rating.after_delivery_hours', '24', 'B-26 (1.31): the rating goes this many hours after a written service is delivered'),
  ('rating.repeat_days', '30', 'B-26 (1.31): one person is asked at most once in this many days'),
  ('rating.since', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'), 'B-26 (1.31): only services finished after this time get a rating (set when migration 0033 ran)')
ON CONFLICT (key) DO NOTHING;

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
  'service_question', 'payment_review_due',
  'rating_low'));

ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check
  CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human', 'system', 'task_rating'));

-- The texts, the owner's words (2026-10-30), quoted word for word in DECISIONS v1.31 (B-01.7 and B-26; the slots as
-- [الاسم الأول] and [الخدمة]). Without every rating text no rating is sent.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('welcome_back', 1, 'أهلًا بحضرتك تاني يا {{first_name}} 👋', 300, true, 'DECISIONS v1.31 B-01', 'owner approval 2026-10-30'),
  ('rating_ask', 1, 'شكرًا إن حضرتك اخترت مكتب الأستاذ سعد في {{service}}. رأيك يهمنا عشان نحسّن خدمتنا، تقيّم الخدمة إزاي؟', 300, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30'),
  ('btn_rating', 1, 'قيّم الخدمة', 20, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30'),
  ('row_rating_5', 1, 'ممتاز', 24, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30'),
  ('row_rating_4', 1, 'جيد جدًا', 24, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30'),
  ('row_rating_3', 1, 'جيد', 24, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30'),
  ('row_rating_2', 1, 'يحتاج للتحسين', 24, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30'),
  ('row_rating_later', 1, 'ليس الآن', 24, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30'),
  ('rating_thanks', 1, 'شكرًا لحضرتك على التقييم 🙏 تحت أمرك في أي وقت.', 300, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30'),
  ('rating_low', 1, 'آسفين إن التجربة ما كانتش زي ما حضرتك عايز. ممكن تقولنا إيه اللي ما عجبكش؟ وحد من المكتب هيتواصل مع حضرتك.', 300, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30'),
  ('rating_later', 1, 'تمام، تحت أمرك في أي وقت.', 300, true, 'DECISIONS v1.31 B-26', 'owner approval 2026-10-30')
ON CONFLICT (key, version) DO NOTHING;

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.service_ratings TO office_api;
GRANT SELECT ON hazem.service_ratings TO office_readonly;
REVOKE ALL ON hazem.service_ratings FROM office_app;

-- migrate:down
UPDATE hazem.approved_texts SET active = false WHERE source IN ('DECISIONS v1.31 B-01', 'DECISIONS v1.31 B-26');
DELETE FROM hazem.approved_texts t
 WHERE t.source IN ('DECISIONS v1.31 B-01', 'DECISIONS v1.31 B-26')
   AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);

DELETE FROM hazem.panel_alerts WHERE kind = 'task_rating';
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check
  CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human', 'system'));

DELETE FROM hazem.staff_tasks WHERE type = 'rating_low';
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
  'service_question', 'payment_review_due'));

DELETE FROM hazem.settings WHERE key IN ('intake.fresh_after_days', 'rating.enabled', 'rating.after_appointment_hours',
  'rating.after_delivery_hours', 'rating.repeat_days', 'rating.since');
DROP TABLE IF EXISTS hazem.service_ratings;
ALTER TABLE hazem.appointments DROP COLUMN IF EXISTS completed_at;
