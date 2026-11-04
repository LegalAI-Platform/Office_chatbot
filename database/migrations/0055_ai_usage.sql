-- 0055_ai_usage: DECISIONS v1.58. The owner (2026-11-04): «المحادثة ما بين الشات بوت والعميل صرفت قد إيه من الكريدت؟ خد كام توكن»,
-- for each conversation and each message. One row per AI call of a turn (the classifier, the writer, its retry, the
-- reviewer): its tokens (the input split into uncached, cache read and cache write, as the prices differ) and its cost in
-- USD at the price of that moment (null: the model has no known price). Counted from this version on; older turns only
-- have the totals of hazem.classifications and hazem.generated_replies.

-- migrate:up
CREATE TABLE hazem.ai_usage (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  turn_id            text NOT NULL REFERENCES hazem.turns (turn_id) ON DELETE CASCADE, -- «انسى الرقم» takes them with the turns
  kind               text NOT NULL CHECK (kind IN ('classify', 'write', 'review')),
  model              text NOT NULL,
  input_tokens       int NOT NULL CHECK (input_tokens >= 0),        -- uncached
  cache_read_tokens  int NOT NULL DEFAULT 0 CHECK (cache_read_tokens >= 0),
  cache_write_tokens int NOT NULL DEFAULT 0 CHECK (cache_write_tokens >= 0),
  output_tokens      int NOT NULL CHECK (output_tokens >= 0),
  cost_usd           numeric(12, 6),
  latency_ms         int,
  created_at         timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ai_usage_turn_idx ON hazem.ai_usage (turn_id);
CREATE INDEX ai_usage_created_idx ON hazem.ai_usage (created_at);
GRANT SELECT, INSERT ON hazem.ai_usage TO office_api;
GRANT SELECT ON hazem.ai_usage TO office_readonly;

-- migrate:down
DROP TABLE hazem.ai_usage;
