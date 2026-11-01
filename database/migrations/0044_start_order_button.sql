-- migrate:up
-- DECISIONS v1.43 (from the conversation of 2026-11-01 03:39, «لو عاوز اعمل عقد عمل بتعلوه» opened an order): words about a
-- written service are a question, not an order. Its text now has this button under it; the order opens only when the client
-- presses it (or the services list's row), or sends his papers. Approved by the owner on 2026-11-01 («نفذ»).

INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('btn_start_order', 1, '✅ ابدأ الطلب', 20, true, 'DECISIONS v1.43 B-16', 'owner approval 2026-11-01')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.source = 'DECISIONS v1.43 B-16'
   AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE source = 'DECISIONS v1.43 B-16';
