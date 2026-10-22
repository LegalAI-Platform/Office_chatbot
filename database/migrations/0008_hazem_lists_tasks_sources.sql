-- 0008_hazem_lists_tasks_sources: TASK 05a (DECISIONS v1.5).
--   B-14 / B-04  the office lists (blocked, the lawyer's direct list, existing clients + their staff)
--   S-05 / B-06  real staff tasks with a due time (replaces the TASK 04 stand-in hazem.pending_tasks)
--   B-13         where a client came from (the Meta referral, now passed to the API)
--   B-25         the topic of the request (matter type) on the intake
--   S-01         abuse warnings (warning once, then muted)
-- Nobody is alerted yet: alerts and the staff's work come with the panel (TASK 07, owner's decision).

-- migrate:up

-- ---------------------------------------------------------------------------
-- Staff and lists
-- ---------------------------------------------------------------------------
CREATE TABLE hazem.staff (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name       text NOT NULL UNIQUE CHECK (length(name) BETWEEN 1 AND 120),
  active     boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE hazem.list_entries (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  list_kind    text NOT NULL CHECK (list_kind IN ('blocked', 'lawyer_direct', 'existing_client')),
  wa_id        text NOT NULL CHECK (wa_id ~ '^[0-9]{8,15}$'),
  name         text CHECK (length(name) <= 120),
  staff_id     uuid REFERENCES hazem.staff (id),
  note         text CHECK (length(note) <= 500),
  import_batch uuid NOT NULL,
  imported_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (list_kind, wa_id)
);
CREATE INDEX list_entries_wa_id_idx ON hazem.list_entries (wa_id);

-- ---------------------------------------------------------------------------
-- Persons (B-13), conversations (S-01 abuse), intakes (B-04, B-14.4, B-25)
-- ---------------------------------------------------------------------------
ALTER TABLE hazem.persons
  ADD COLUMN source        text CHECK (source IN ('ad', 'link', 'direct')),
  ADD COLUMN source_raw    jsonb,
  ADD COLUMN first_seen_at timestamptz;

ALTER TABLE hazem.conversations
  ADD COLUMN abuse_warnings int NOT NULL DEFAULT 0 CHECK (abuse_warnings >= 0);

ALTER TABLE hazem.intakes
  ADD COLUMN sender_role             text CHECK (sender_role IN ('client', 'lawyer_colleague', 'press', 'vendor')),
  ADD COLUMN existing_question_asked boolean NOT NULL DEFAULT false,
  ADD COLUMN matter_type             text,
  ADD COLUMN matter_subtype          text,
  ADD COLUMN matter_asked            boolean NOT NULL DEFAULT false;

ALTER TABLE hazem.classifications
  ADD COLUMN matter_type    text,
  ADD COLUMN matter_subtype text,
  ADD COLUMN sender_role    text;

-- ---------------------------------------------------------------------------
-- Staff tasks (S-05). Types: the eight of S-05 plus the TASK 04/05 ones, documented in the report.
-- ---------------------------------------------------------------------------
CREATE TABLE hazem.staff_tasks (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  type       text NOT NULL CHECK (type IN (
               'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
               'complaint', 'human_requested', 'safety_alert',
               'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
               'service_request', 'reschedule_or_cancel')),
  state      text NOT NULL DEFAULT 'open' CHECK (state IN ('open', 'in_progress', 'done', 'overdue', 'cancelled')),
  staff_id   uuid REFERENCES hazem.staff (id),
  person_id  uuid NOT NULL REFERENCES hazem.persons (id),
  intake_id  uuid,
  turn_id    text NOT NULL,
  due_at     timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (turn_id, type)
);
CREATE INDEX staff_tasks_open_idx ON hazem.staff_tasks (due_at) WHERE state IN ('open', 'in_progress');
CREATE TRIGGER trg_staff_tasks_updated_at BEFORE UPDATE ON hazem.staff_tasks
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- The TASK 04 stand-in rows move over (their due time is unknown: the time they were made).
INSERT INTO hazem.staff_tasks (person_id, turn_id, type, state, due_at, created_at)
SELECT person_id, turn_id, kind, CASE status WHEN 'done' THEN 'done' WHEN 'cancelled' THEN 'cancelled' ELSE 'open' END,
       created_at, created_at
  FROM hazem.pending_tasks;
DROP TABLE hazem.pending_tasks;

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.staff, hazem.list_entries, hazem.staff_tasks TO office_api;
GRANT SELECT ON hazem.staff, hazem.list_entries, hazem.staff_tasks TO office_readonly;
REVOKE ALL ON hazem.staff, hazem.list_entries, hazem.staff_tasks FROM office_app;

-- ---------------------------------------------------------------------------
-- Settings and texts
-- ---------------------------------------------------------------------------
INSERT INTO hazem.settings (key, value, description) VALUES
  ('handoff.enabled', 'false',
   'true = «موظف» hands the conversation to the staff and Hazem stays silent (B-12). Stays false until the panel (TASK 07): nobody would answer'),
  ('source.link_codes', '',
   'Comma-separated codes put in wa.me links; a first message that contains one is recorded as source link (B-13)');

-- From DECISIONS v1.5, word for word (active).
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('existing_or_new', 1, 'حضرتك بتتابع موضوع قائم مع المكتب، ولا عايز استشارة في موضوع جديد؟', 300, true, 'DECISIONS v1.5 B-04', 'migration 0008'),
  ('ask_topic', 1, 'ولو تحب، قولّي باختصار الموضوع عن إيه عشان الأستاذ يبقى عارفه.', 300, true, 'DECISIONS v1.5 B-25', 'migration 0008'),
  ('topic_received', 1, 'تمام، وصلت ✅', 300, true, 'DECISIONS v1.5 B-25', 'migration 0008');

-- Proposed in TASK 05a and approved by the owner as proposed (2026-10-22, in chat): active.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('abuse_warning', 1, 'معلش، خلّينا نتكلم باحترام عشان نقدر نساعد حضرتك.', 300, true, 'proposal TASK 05a', 'owner approval 2026-10-22'),
  ('lawyer_direct_received', 1, 'أهلًا بحضرتك، رسالتك وصلت للأستاذ سعد.', 300, true, 'proposal TASK 05a', 'owner approval 2026-10-22'),
  ('colleague_received', 1, 'أهلًا بحضرتك يا أستاذ، رسالتك وصلت للمكتب.', 300, true, 'proposal TASK 05a', 'owner approval 2026-10-22'),
  ('press_received', 1, 'أهلًا بحضرتك، رسالتك وصلت للمكتب.', 300, true, 'proposal TASK 05a', 'owner approval 2026-10-22'),
  ('vendor_received', 1, 'أهلًا بحضرتك، رسالتك وصلت للمكتب.', 300, true, 'proposal TASK 05a', 'owner approval 2026-10-22');

-- ---------------------------------------------------------------------------
-- B-13: the API now receives the Meta referral of a message (ad / link), without image or video
-- links. The 0004 function is kept and wrapped, so the shape of every other field is unchanged.
-- ---------------------------------------------------------------------------
ALTER FUNCTION app.wa_message_json(uuid) RENAME TO wa_message_json_0004;

CREATE FUNCTION app.wa_message_json(p_message_id uuid) RETURNS jsonb
LANGUAGE sql STABLE SET search_path = app, public AS $$
  SELECT wa_message_json_0004(m.id)
         || CASE WHEN m.referral IS NULL OR jsonb_typeof(m.referral) <> 'object' THEN '{}'::jsonb
                 ELSE jsonb_build_object('referral', jsonb_strip_nulls(jsonb_build_object(
                        'source_type', left(m.referral->>'source_type', 50),
                        'source_id',   left(m.referral->>'source_id', 100),
                        'headline',    left(m.referral->>'headline', 200)))) END
    FROM wa_inbound_messages m WHERE m.id = p_message_id
$$;

-- migrate:down
DROP FUNCTION IF EXISTS app.wa_message_json(uuid);
ALTER FUNCTION app.wa_message_json_0004(uuid) RENAME TO wa_message_json;

DELETE FROM hazem.approved_texts WHERE source IN ('proposal TASK 05a', 'DECISIONS v1.5 B-04', 'DECISIONS v1.5 B-25')
  AND key IN ('existing_or_new', 'ask_topic', 'topic_received', 'abuse_warning', 'lawyer_direct_received',
              'colleague_received', 'press_received', 'vendor_received');
DELETE FROM hazem.settings WHERE key IN ('handoff.enabled', 'source.link_codes');

CREATE TABLE hazem.pending_tasks (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  person_id  uuid NOT NULL REFERENCES hazem.persons (id),
  turn_id    text NOT NULL,
  kind       text NOT NULL CHECK (kind IN ('existing_client_message', 'human_requested', 'unclear_message',
               'urgent_request', 'service_request', 'reschedule_or_cancel')),
  status     text NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'done', 'cancelled')),
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (turn_id, kind)
);
CREATE INDEX pending_tasks_open_idx ON hazem.pending_tasks (created_at) WHERE status = 'open';
INSERT INTO hazem.pending_tasks (person_id, turn_id, kind, status, created_at)
SELECT person_id, turn_id, type,
       CASE state WHEN 'done' THEN 'done' WHEN 'cancelled' THEN 'cancelled' ELSE 'open' END, created_at
  FROM hazem.staff_tasks
 WHERE type IN ('existing_client_message', 'human_requested', 'unclear_message', 'urgent_request',
                'service_request', 'reschedule_or_cancel');
GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.pending_tasks TO office_api;
GRANT SELECT ON hazem.pending_tasks TO office_readonly;
REVOKE ALL ON hazem.pending_tasks FROM office_app;

DROP TABLE IF EXISTS hazem.staff_tasks;
ALTER TABLE hazem.classifications DROP COLUMN IF EXISTS matter_type, DROP COLUMN IF EXISTS matter_subtype,
  DROP COLUMN IF EXISTS sender_role;
ALTER TABLE hazem.intakes DROP COLUMN IF EXISTS sender_role, DROP COLUMN IF EXISTS existing_question_asked,
  DROP COLUMN IF EXISTS matter_type, DROP COLUMN IF EXISTS matter_subtype, DROP COLUMN IF EXISTS matter_asked;
ALTER TABLE hazem.conversations DROP COLUMN IF EXISTS abuse_warnings;
ALTER TABLE hazem.persons DROP COLUMN IF EXISTS source, DROP COLUMN IF EXISTS source_raw,
  DROP COLUMN IF EXISTS first_seen_at;
DROP TABLE IF EXISTS hazem.list_entries;
DROP TABLE IF EXISTS hazem.staff;
