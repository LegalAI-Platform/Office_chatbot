-- 0012_panel_alerts: TASK 07c (DECISIONS v1.11). The office's alerts, and what the panel's new
-- tools write to the audit.
--   hazem.panel_alerts       one alert per task and kind: a task overdue (S-05), urgent (B-09) or
--                            a client asking for a person (B-12). The panel's bell shows the ones a
--                            person may see (a staff member: theirs and the unassigned); an e-mail
--                            goes out once (emailed_at). Never the client's words.
--   hazem.panel_alert_reads  who has read which alert.
-- Tasks from before this migration make no alert (alerts.since).
-- hazem.panel_audit also records the approved texts, the lists and unmuting (target_type).
-- hazem.approved_texts gets created_at: when a version was written (updated_at moves when it is
-- switched on or off), for the panel's history of versions.

-- migrate:up
CREATE TABLE hazem.panel_alerts (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  kind       text NOT NULL CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human')),
  task_id    uuid NOT NULL REFERENCES hazem.staff_tasks (id) ON DELETE CASCADE,
  created_at timestamptz NOT NULL DEFAULT now(),
  emailed_at timestamptz,
  UNIQUE (kind, task_id)
);
CREATE INDEX panel_alerts_created_idx ON hazem.panel_alerts (created_at DESC);
CREATE INDEX panel_alerts_unsent_idx ON hazem.panel_alerts (created_at) WHERE emailed_at IS NULL;

CREATE TABLE hazem.panel_alert_reads (
  alert_id bigint NOT NULL REFERENCES hazem.panel_alerts (id) ON DELETE CASCADE,
  user_id  uuid NOT NULL REFERENCES hazem.panel_users (id) ON DELETE CASCADE,
  read_at  timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (alert_id, user_id)
);

INSERT INTO hazem.settings (key, value, description) VALUES
  ('alerts.since', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
   'Tasks from this time on make alerts (TASK 07c); older ones do not')
ON CONFLICT (key) DO NOTHING;

ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation'));

ALTER TABLE hazem.approved_texts ADD COLUMN created_at timestamptz;
UPDATE hazem.approved_texts SET created_at = updated_at;
ALTER TABLE hazem.approved_texts ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN created_at SET DEFAULT now();

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.panel_alerts, hazem.panel_alert_reads TO office_api;
GRANT USAGE, SELECT ON SEQUENCE hazem.panel_alerts_id_seq TO office_api;
GRANT SELECT ON hazem.panel_alerts, hazem.panel_alert_reads TO office_readonly;
REVOKE ALL ON hazem.panel_alerts, hazem.panel_alert_reads FROM office_app;

-- migrate:down
ALTER TABLE hazem.approved_texts DROP COLUMN IF EXISTS created_at;
DELETE FROM hazem.panel_audit WHERE target_type IN ('text', 'list', 'conversation');
ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check CHECK (target_type IN ('user', 'task'));
DELETE FROM hazem.settings WHERE key = 'alerts.since';
DROP TABLE IF EXISTS hazem.panel_alert_reads;
DROP TABLE IF EXISTS hazem.panel_alerts;
