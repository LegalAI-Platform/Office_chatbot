-- migrate:up
-- DECISIONS v1.111 (the owner, 2026-11-14: «الاشعارات اللي فوق الي هي بتظهر كمان في الويندوز انا عاوزك تضيف فيها لو عميل
-- بعت صوره الايصال وعاوز تأكيد الدفع»). A receipt that waits for the office's review (the payment proof_received or
-- duplicate_suspected, as «الإيصالات» lists it) is a «receipt_review» alert: the bell and Windows, never an e-mail. One per
-- payment each time it waits again (its first receipt since the last review). It is read for everyone once the payment is
-- reviewed. The urgent call's receipt has its own urgent alert (B-09), so it makes none here. No task: «الإيصالات» is the
-- office's list of them.

ALTER TABLE hazem.panel_alerts ADD COLUMN receipt_id uuid REFERENCES hazem.payment_receipts (id) ON DELETE CASCADE;
CREATE UNIQUE INDEX panel_alerts_receipt_idx ON hazem.panel_alerts (receipt_id) WHERE receipt_id IS NOT NULL;

ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check
  CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human', 'system', 'task_rating', 'task_new', 'receipt_review'));

-- a task's alert has its task; a system alert its what and its ref; a receipt's alert its receipt, and nothing else
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_system_shape;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_system_shape CHECK (
  (kind = 'system') = (system_what IS NOT NULL) AND (kind = 'system') = (system_ref IS NOT NULL)
  AND (kind = 'receipt_review') = (receipt_id IS NOT NULL)
  AND (kind IN ('system', 'receipt_review')) = (task_id IS NULL));

-- migrate:down
DELETE FROM hazem.panel_alerts WHERE kind = 'receipt_review';
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_system_shape;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_system_shape CHECK (
  (kind = 'system') = (task_id IS NULL) AND (kind = 'system') = (system_what IS NOT NULL) AND (kind = 'system') = (system_ref IS NOT NULL));
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check
  CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human', 'system', 'task_rating', 'task_new'));
DROP INDEX IF EXISTS hazem.panel_alerts_receipt_idx;
ALTER TABLE hazem.panel_alerts DROP COLUMN receipt_id;
