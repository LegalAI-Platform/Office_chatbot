-- 0011_chatwoot: TASK 07b (DECISIONS T-24). What links our conversations to Chatwoot's.
--   hazem.chatwoot_threads  one Chatwoot contact and conversation per client (wa_id).
--   hazem.chatwoot_turns    which turns were copied into Chatwoot (the client's words and Hazem's
--                           replies), and how many of a turn's messages got there (a retry resumes
--                           after them); a failure is tried again later, with a growing wait.
--   hazem.chatwoot_replies  every staff message Chatwoot announced, once: a webhook delivered
--                           twice never sends twice. Holds ids and an error code, never the text.
-- Turns from before this migration are not copied (chatwoot.mirror_since).
-- handoff.enabled is NOT changed here: chatwoot-setup turns it on once Chatwoot can answer.

-- migrate:up
CREATE TABLE hazem.chatwoot_threads (
  wa_id           text PRIMARY KEY CHECK (wa_id ~ '^[0-9]{8,15}$'),
  contact_id      bigint NOT NULL,
  source_id       text NOT NULL CHECK (length(source_id) BETWEEN 1 AND 200),
  conversation_id bigint NOT NULL UNIQUE,
  created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE hazem.chatwoot_turns (
  turn_id    text PRIMARY KEY,
  state      text NOT NULL CHECK (state IN ('done', 'failed')),
  attempts   int NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 1000),
  posted     int NOT NULL DEFAULT 0 CHECK (posted >= 0),
  next_at    timestamptz,
  last_error text CHECK (last_error IS NULL OR length(last_error) <= 300),
  updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX chatwoot_turns_retry_idx ON hazem.chatwoot_turns (next_at) WHERE state = 'failed';

CREATE TABLE hazem.chatwoot_replies (
  message_id      bigint PRIMARY KEY,
  conversation_id bigint NOT NULL,
  wa_id           text NOT NULL CHECK (wa_id ~ '^[0-9]{8,15}$'),
  user_id         bigint,
  state           text NOT NULL CHECK (state IN ('sending', 'sent', 'failed', 'unknown', 'skipped')),
  error           text CHECK (error IS NULL OR length(error) <= 300),
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now()
);

INSERT INTO hazem.settings (key, value, description) VALUES
  ('chatwoot.mirror_since', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
   'Turns from this time on are copied into Chatwoot (TASK 07b); older ones are not')
ON CONFLICT (key) DO NOTHING;

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.chatwoot_threads, hazem.chatwoot_turns, hazem.chatwoot_replies TO office_api;
GRANT SELECT ON hazem.chatwoot_threads, hazem.chatwoot_turns, hazem.chatwoot_replies TO office_readonly;
REVOKE ALL ON hazem.chatwoot_threads, hazem.chatwoot_turns, hazem.chatwoot_replies FROM office_app;

-- migrate:down
UPDATE hazem.settings SET value = 'false' WHERE key = 'handoff.enabled'; -- nobody would answer without Chatwoot
DELETE FROM hazem.settings WHERE key = 'chatwoot.mirror_since';
DROP TABLE IF EXISTS hazem.chatwoot_replies;
DROP TABLE IF EXISTS hazem.chatwoot_turns;
DROP TABLE IF EXISTS hazem.chatwoot_threads;
