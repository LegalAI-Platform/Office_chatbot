-- 0005_hazem_conversations: Hazem's own data for TASK 04 (schema hazem, owned by the API through
-- office_api). Persons, conversations (S-01), intakes (S-02), approved texts, the AI
-- classification log (no client text), which approved text each reply used, and a simple task
-- list that stands in for S-05 until TASK 05/07.
-- Appointments, payments, staff tasks and Urfi cases have code and tests only in TASK 04; their
-- tables come with their tasks (05, 08, 09, 11).

-- migrate:up

CREATE TABLE hazem.persons (
  id                   uuid PRIMARY KEY,
  wa_id                text NOT NULL UNIQUE CHECK (wa_id ~ '^[0-9]{8,15}$'),
  name_raw             text CHECK (length(name_raw) <= 120),
  name_normalized      text CHECK (length(name_normalized) <= 120),
  full_name_provided   boolean,                 -- null = not settled yet (B-02)
  preferred_call_phone text CHECK (preferred_call_phone ~ '^[0-9]{8,15}$'),  -- B-03, calls only
  created_at           timestamptz NOT NULL DEFAULT now(),
  updated_at           timestamptz NOT NULL DEFAULT now()
);
CREATE TRIGGER trg_persons_updated_at BEFORE UPDATE ON hazem.persons
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

CREATE TABLE hazem.conversations (
  id                    uuid PRIMARY KEY,
  person_id             uuid NOT NULL UNIQUE REFERENCES hazem.persons (id),
  state                 text NOT NULL CHECK (state IN ('bot_active', 'human_handoff', 'muted', 'ignored')),
  last_staff_message_at timestamptz,
  created_at            timestamptz NOT NULL DEFAULT now(),
  updated_at            timestamptz NOT NULL DEFAULT now()
);
CREATE TRIGGER trg_conversations_updated_at BEFORE UPDATE ON hazem.conversations
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

CREATE TABLE hazem.intakes (
  id                        uuid PRIMARY KEY,
  person_id                 uuid NOT NULL REFERENCES hazem.persons (id),
  state                     text NOT NULL CHECK (state IN ('new', 'awaiting_name', 'identifying', 'classified',
                              'in_service_flow', 'offer_sent', 'followup_sent', 'handed_to_staff', 'closed')),
  client_type               text CHECK (client_type IN ('new_client', 'existing_client', 'lawyer_direct',
                              'blocked', 'colleague', 'press', 'vendor')),
  name_asks                 int NOT NULL DEFAULT 0 CHECK (name_asks BETWEEN 0 AND 2),
  -- B-01.4: what the client asked before giving the name, answered right after it.
  pending_intent            text,
  pending_service           text,
  pending_consultation_mode text,
  unclear_in_a_row          int NOT NULL DEFAULT 0 CHECK (unclear_in_a_row >= 0),
  created_at                timestamptz NOT NULL DEFAULT now(),
  updated_at                timestamptz NOT NULL DEFAULT now()
);
-- One open request per person; a closed one stays as history.
CREATE UNIQUE INDEX intakes_one_open_idx ON hazem.intakes (person_id) WHERE state <> 'closed';
CREATE TRIGGER trg_intakes_updated_at BEFORE UPDATE ON hazem.intakes
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- Every word Hazem sends is one of these rows (T-07). Changing a text = a new version.
CREATE TABLE hazem.approved_texts (
  key        text NOT NULL CHECK (key ~ '^[a-z][a-z0-9_]{1,63}$'),
  version    int NOT NULL CHECK (version >= 1),
  text       text NOT NULL CHECK (length(text) BETWEEN 1 AND 1200),
  max_chars  int NOT NULL CHECK (max_chars IN (300, 600)),   -- B-21
  active     boolean NOT NULL DEFAULT false,
  source     text NOT NULL,                                   -- 'DECISIONS v1.4 B-01' or 'proposal TASK 04'
  updated_at timestamptz NOT NULL DEFAULT now(),
  updated_by text,
  PRIMARY KEY (key, version)
);
CREATE UNIQUE INDEX approved_texts_one_active_idx ON hazem.approved_texts (key) WHERE active;
CREATE TRIGGER trg_approved_texts_updated_at BEFORE UPDATE ON hazem.approved_texts
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

-- Which approved text (and version) went out under which idempotency key.
CREATE TABLE hazem.turn_replies (
  idempotency_key text PRIMARY KEY,
  turn_id         text NOT NULL,
  text_key        text NOT NULL,
  text_version    int NOT NULL,
  part            int NOT NULL DEFAULT 0,     -- a long text split at a natural point (B-21)
  instead_of      text,                       -- the key that could not be sent
  instead_reason  text,
  created_at      timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY (text_key, text_version) REFERENCES hazem.approved_texts (key, version)
);
CREATE INDEX turn_replies_turn_idx ON hazem.turn_replies (turn_id);

-- One row per AI classification. NO client text and NO name: only what was decided and its cost.
CREATE TABLE hazem.classifications (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  turn_id           text NOT NULL UNIQUE,
  intent            text NOT NULL,
  confidence        numeric(4, 3) NOT NULL CHECK (confidence BETWEEN 0 AND 1),
  service           text,
  consultation_mode text,
  full_name_present boolean NOT NULL,
  model             text,
  prompt_version    text,
  latency_ms        int,
  input_tokens      int,
  output_tokens     int,
  fallback_reason   text,                     -- set when the answer was replaced by intent other
  created_at        timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX classifications_created_idx ON hazem.classifications (created_at);

-- TASK 04 stand-in for S-05: nobody works these yet (TASK 05/07).
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

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.persons, hazem.conversations, hazem.intakes,
  hazem.approved_texts, hazem.turn_replies, hazem.classifications, hazem.pending_tasks TO office_api;
GRANT SELECT ON hazem.persons, hazem.conversations, hazem.intakes, hazem.approved_texts,
  hazem.turn_replies, hazem.classifications, hazem.pending_tasks TO office_readonly;
-- The database-wide default privileges also grant office_app: take that back (as in 0003).
REVOKE ALL ON hazem.persons, hazem.conversations, hazem.intakes, hazem.approved_texts,
  hazem.turn_replies, hazem.classifications, hazem.pending_tasks FROM office_app;

INSERT INTO hazem.settings (key, value, description) VALUES
  ('responder',          'hazem', 'hazem = understand and answer from approved texts; echo = the TASK 02/03 test echo (hazem-e2e only)'),
  ('hazem.recipients',   '',      'Comma-separated wa_id list Hazem answers while hazem.open_to_all is false'),
  ('hazem.open_to_all',  'false', 'true = Hazem answers every number. Only on the owner''s explicit decision (CLAUDE.md)'),
  ('ai.min_confidence',  '0.6',   'Below this confidence the turn is treated as unclear (intent other)'),
  ('ai.stub_enabled',    'false', 'true only inside hazem-e2e (and only when ENVIRONMENT=test): classify through ai.stub_base_url with a dummy key'),
  ('ai.stub_base_url',   '',      'The test stub hazem-e2e points the classifier at');

-- Texts from DECISIONS v1.4, word for word: active.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('welcome_ask_name', 1, 'أهلًا بحضرتك في مكتب الأستاذ سعد فتحي سعد للمحاماة، معاك حازم. ممكن اسم حضرتك رباعي؟', 300, true, 'DECISIONS v1.4 B-01', 'migration 0005'),
  ('consultation_offer', 1, 'الأستاذ يقدر يشوف الموضوع مع حضرتك في استشارة: بالتليفون بـ500 أو في المكتب بـ1,000.', 300, true, 'DECISIONS v1.4 B-11 (owner: the general consultation offer)', 'migration 0005'),
  ('legal_question', 1, 'الأستاذ هو اللي هيجاوب حضرتك على ده في الاستشارة.', 300, true, 'DECISIONS v1.4 B-20', 'migration 0005'),
  ('urgent_price', 1, 'الاستشارة العاجلة بالتليفون ربع ساعة بـ1,000 جنيه.', 300, true, 'DECISIONS v1.4 B-09', 'migration 0005'),
  ('document_review_request', 1, 'تحت أمرك. مراجعة العقد بتبدأ من 1,500 جنيه. ابعتلي صورة العقد هنا.', 300, true, 'DECISIONS v1.4 B-16', 'migration 0005'),
  ('written_opinion', 1, 'الرأي المكتوب بيكون بعد الاستشارة، وسعره سعر الاستشارة + 2,000 جنيه.', 300, true, 'DECISIONS v1.4 B-17', 'migration 0005'),
  ('urf_unclear', 1, 'تحت أمرك، قولّي المطلوب', 300, true, 'DECISIONS v1.4 B-11', 'migration 0005'),
  ('urf_proof', 1, 'إثبات الجواز بيكون بدعوى قدام محكمة الأسرة، والأستاذ هيحدد المطلوب حسب الورق اللي معاك.', 300, true, 'DECISIONS v1.4 B-11', 'migration 0005');

-- Proposed texts (docs/approved-texts-proposal.md): inactive until the owner approves them.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('ask_name_again', 1, 'معلش، ممكن اسم حضرتك رباعي؟', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('unclear', 1, 'معلش مش واضحلي المطلوب. ممكن تكتبلي حضرتك محتاج إيه؟', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('unclear_again', 1, 'تمام، هبلّغ المكتب برسالة حضرتك.', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('greeting', 1, 'أهلًا بحضرتك 👋 تحت أمرك، محتاج إيه؟', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('thanks', 1, 'العفو، تحت أمر حضرتك في أي وقت.', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('human_request', 1, 'تمام، هبلّغ المكتب إن حضرتك عايز تكلم حد.', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('existing_matter', 1, 'رسالة حضرتك وصلت للمكتب، وهتتبلّغ للمسؤول عن موضوعك.', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('reschedule_or_cancel', 1, 'تمام، هبلّغ المكتب بطلب حضرتك بخصوص الميعاد.', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('contract_drafting_request', 1, 'صياغة العقد بتبدأ من 3,000 جنيه حسب نوعه. ابعتلي أي تفاصيل أو ورق عندك.', 300, false, 'proposal TASK 04 (B-16 without the contact promise)', 'migration 0005'),
  ('formal_notice_request', 1, 'الإنذار الرسمي بـ2,000 جنيه. ابعتلي الورق اللي عندك.', 300, false, 'proposal TASK 04 (B-16 without the contact promise)', 'migration 0005'),
  ('urf_request', 1, 'تحت أمرك. طلب حضرتك وصل للمكتب، وهنكمل معاك فيه.', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('price_document_review', 1, 'مراجعة المستندات أو العقد من 1,500 لحد 3,000 جنيه، والأستاذ بيحدد السعر قبل الدفع.', 300, false, 'proposal TASK 04 (B-23)', 'migration 0005'),
  ('price_contract_drafting', 1, 'صياغة العقد بتبدأ من 3,000 جنيه، والأستاذ بيحدد السعر النهائي حسب نوع العقد.', 300, false, 'proposal TASK 04 (B-23)', 'migration 0005'),
  ('price_formal_notice', 1, 'الإنذار الرسمي بـ2,000 جنيه.', 300, false, 'proposal TASK 04 (B-23)', 'migration 0005'),
  ('price_urf', 1, 'الجواز أو الطلاق العرفي بـ5,000 جنيه لو الشهود من المكتب، و4,000 لو الشهود من طرف حضرتك.', 300, false, 'proposal TASK 04 (B-23)', 'migration 0005'),
  ('price_case_representation', 1, 'أتعاب مباشرة القضية بتتحدد بعد الاستشارة مع الأستاذ.', 300, false, 'proposal TASK 04 (B-23)', 'migration 0005'),
  ('media_received', 1, 'استلمت {{media}} ✅', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('media_too_large', 1, 'الملف كبير ومقدرتش أستلمه. ممكن تبعته بحجم أصغر؟', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('media_failed', 1, 'الملف ما وصلش كويس. ممكن تبعته تاني؟', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('media_rejected', 1, 'النوع ده من الملفات مش بنستقبله. ممكن تبعته صورة أو PDF؟', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('voice_failed', 1, 'معلش مقدرتش أسمع الرسالة الصوتية كويس. ممكن تكتبلي المطلوب؟', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('unsupported', 1, 'معلش، النوع ده من الرسايل مش بقدر أقراه. ممكن تكتبلي المطلوب؟', 300, false, 'proposal TASK 04', 'migration 0005'),
  ('location_received', 1, 'استلمت اللوكيشن ✅', 300, false, 'proposal TASK 04', 'migration 0005');

-- migrate:down
DELETE FROM hazem.settings WHERE key IN ('responder', 'hazem.recipients', 'hazem.open_to_all',
  'ai.min_confidence', 'ai.stub_enabled', 'ai.stub_base_url');
DROP TABLE IF EXISTS hazem.pending_tasks;
DROP TABLE IF EXISTS hazem.classifications;
DROP TABLE IF EXISTS hazem.turn_replies;
DROP TABLE IF EXISTS hazem.approved_texts;
DROP TABLE IF EXISTS hazem.intakes;
DROP TABLE IF EXISTS hazem.conversations;
DROP TABLE IF EXISTS hazem.persons;
