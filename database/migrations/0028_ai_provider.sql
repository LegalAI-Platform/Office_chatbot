-- 0028_ai_provider: DECISIONS v1.26 (T-07/T-08). The owner (2026-10-28): «اشتغل ب anthropic موقتاً… عاوز الاتنين يبقو
-- موجودين». ai.provider picks the classifier that answers first; when it gives no answer (no key, no credits, an error)
-- the other one is asked in the same turn. Both use the same prompt and the same closed lists.

-- migrate:up
INSERT INTO hazem.settings (key, value, description) VALUES
  ('ai.provider', 'openai', 'T-08 (1.26): openai or anthropic, the classifier asked first; the other answers when it fails')
ON CONFLICT (key) DO NOTHING;

-- migrate:down
DELETE FROM hazem.settings WHERE key = 'ai.provider';
