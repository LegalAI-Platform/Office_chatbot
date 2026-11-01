-- migrate:up
-- TASK 17 (DECISIONS v1.41): Hazem talks like a person. The code decides what is said; the AI («the writer») says the
-- everyday replies in its own words, seeing the conversation; the code checks every sentence before it goes.
-- 1. hazem.generated_replies: each written reply, the text sent (null when the approved text went instead) and «guard»:
--    passed, or why it was not used (a check, or the writer's failure). Hazem's own outbound words only: no client text.
-- 2. The switch (off: exactly the approved texts, as before) and the writer's model (empty: the classifier's).

CREATE TABLE hazem.generated_replies (
  idempotency_key text PRIMARY KEY,
  turn_id         text NOT NULL REFERENCES hazem.turns (turn_id) ON DELETE CASCADE, -- «انسى الرقم» takes them with the turns
  purpose         text NOT NULL,                -- the approved text's key it replaces
  text            text CHECK (text IS NULL OR char_length(text) BETWEEN 1 AND 300),
  guard           text NOT NULL,                -- 'passed' or the reason the approved text went
  model           text NOT NULL,
  prompt_version  text NOT NULL,
  latency_ms      int NOT NULL,
  input_tokens    int,
  output_tokens   int,
  created_at      timestamptz NOT NULL DEFAULT now(),
  CHECK ((guard = 'passed') = (text IS NOT NULL))
);
CREATE INDEX generated_replies_turn_idx ON hazem.generated_replies (turn_id);
CREATE INDEX generated_replies_created_idx ON hazem.generated_replies (created_at);
GRANT SELECT, INSERT ON hazem.generated_replies TO office_api;
GRANT SELECT ON hazem.generated_replies TO office_readonly;

INSERT INTO hazem.settings (key, value, description) VALUES
  ('hazem.conversational', 'false', 'v1.41 (TASK 17): the everyday replies in the writer''s words (true), or exactly the approved texts'),
  ('ai.writer_model', '', 'v1.41 (TASK 17): the writer''s model (empty: the classifier''s model)')
ON CONFLICT (key) DO NOTHING;

-- migrate:down
DELETE FROM hazem.settings WHERE key IN ('hazem.conversational', 'ai.writer_model');
DROP TABLE hazem.generated_replies;
