-- v1.108 (the owner, 2026-11-13): «لو عميل جه وحجز بتلاقي فيه مليون حاجة بتحصل ومليون مكان يتبعت فيه». One booking is one
-- task at a time, and an alert goes only to whoever's turn it is.
--   hazem.staff_tasks.appointment_id   the booking a task belongs to. The panel shows the booking with the task.
--   hazem.staff_tasks.escalated_at     an urgent call nobody took in time. The same task then goes to the staff (the owner:
--                                      «الموظف»), instead of a new «urgent_call_unanswered» task.
--   hazem.appointments.urgent_alert_failed_at   the lawyer's WhatsApp alert could not go (no number, a refused send). The
--                                      task says so, instead of a new «urgent_call_failed» task.
--   hazem.panel_alerts                 gains the kind «task_escalated»: that urgent task reached the staff.
-- The booking's own tasks already open («booking:<appointment>:…») are linked to their booking.

-- migrate:up
ALTER TABLE hazem.staff_tasks
  ADD COLUMN appointment_id uuid REFERENCES hazem.appointments (id) ON DELETE SET NULL,
  ADD COLUMN escalated_at   timestamptz;
CREATE INDEX staff_tasks_appointment_idx ON hazem.staff_tasks (appointment_id) WHERE appointment_id IS NOT NULL;

ALTER TABLE hazem.appointments ADD COLUMN urgent_alert_failed_at timestamptz;

ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check
  CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human', 'system', 'task_rating', 'task_new', 'task_escalated'));

-- compared as text: a turn id that only looks like one is never cast
UPDATE hazem.staff_tasks t SET appointment_id = a.id
  FROM hazem.appointments a
 WHERE t.appointment_id IS NULL AND t.turn_id ~ '^booking:[0-9a-f]{32}:'
   AND replace(a.id::text, '-', '') = substring(t.turn_id FROM 9 FOR 32);

-- migrate:down
DELETE FROM hazem.panel_alerts WHERE kind = 'task_escalated';
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check
  CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human', 'system', 'task_rating', 'task_new'));
ALTER TABLE hazem.appointments DROP COLUMN urgent_alert_failed_at;
DROP INDEX hazem.staff_tasks_appointment_idx;
ALTER TABLE hazem.staff_tasks DROP COLUMN escalated_at, DROP COLUMN appointment_id;
