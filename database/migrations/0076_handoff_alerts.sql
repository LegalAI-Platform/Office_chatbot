-- v1.105 (the owner, 2026-11-12): «العميل دخل عندي على السيستم واجابته لازم موظف يتدخل ليه فيها … الحاجات دي انا عايزها
-- تيجي في النوتيفيكيشن … مش هنمشي كيس. انا نشوف حل عام».
--   hazem.panel_alerts    gains the kind «task_new»: every task Hazem opens for the office alerts the people who may see
--                         it at once, whatever its type. An urgent request, a client asking for someone and a low rating
--                         keep their own kinds. It shows in the bell and as a Windows notification; it is not e-mailed.

-- migrate:up
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check
  CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human', 'system', 'task_rating', 'task_new'));

-- migrate:down
DELETE FROM hazem.panel_alerts WHERE kind = 'task_new';
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check
  CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human', 'system', 'task_rating'));
