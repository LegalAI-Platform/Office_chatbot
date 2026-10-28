-- 0026_classification_scope: DECISIONS v1.24 (B-20). The AI now decides the subject of a turn first (scope: office or
-- outside); outside is answered with off_topic by the code, whatever intent came with it. The scope the model gave is
-- kept with each classification (null for older rows and for calls without an answer).

-- migrate:up
ALTER TABLE hazem.classifications ADD COLUMN scope text;

-- migrate:down
ALTER TABLE hazem.classifications DROP COLUMN IF EXISTS scope;
