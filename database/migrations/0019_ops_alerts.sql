-- 0019_ops_alerts: TASK 13 (DECISIONS v1.18, T-15 / T-23): the nightly jobs and the system's own alerts.
--   hazem.ops_runs        one row per run of a job of the tools container: the backup (databases and files), the
--                         healthcheck, the monthly restore drill, the log scan. When it started and ended, whether
--                         it worked, its size and an error code. Never a client's data, a file name or a key.
--   hazem.panel_alerts    gains the kind «system» (no task): a failed job, or n8n workflows that failed in an hour.
--                         The managers see it in the bell and get an e-mail (no client data).
--   hazem.n8n_error_hours how many n8n workflows failed in each hour: counts only (the API never reads n8n's schema,
--                         migration 0003; the messages stay there).
-- The tools container writes ops_runs as the database owner; the API reads it.

-- migrate:up
CREATE TABLE hazem.ops_runs (
  id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  kind        text NOT NULL CHECK (kind IN ('backup', 'healthcheck', 'drill', 'log_scan')),
  started_at  timestamptz NOT NULL DEFAULT now(),
  finished_at timestamptz,
  ok          boolean,
  size_bytes  bigint CHECK (size_bytes >= 0),
  items       int CHECK (items >= 0),           -- files copied, checks passed, tables verified, lines scanned
  error_code  text CHECK (length(error_code) <= 120),
  CHECK ((finished_at IS NULL) = (ok IS NULL))
);
CREATE INDEX ops_runs_kind_idx ON hazem.ops_runs (kind, started_at DESC);

ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check
  CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human', 'system'));
ALTER TABLE hazem.panel_alerts ALTER COLUMN task_id DROP NOT NULL;
ALTER TABLE hazem.panel_alerts
  ADD COLUMN system_what text CHECK (system_what IN ('backup', 'healthcheck', 'drill', 'log_scan', 'n8n')),
  ADD COLUMN system_ref  text UNIQUE CHECK (length(system_ref) <= 80);  -- run:<id> or n8n:<hour>: one alert each
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_system_shape CHECK (
  (kind = 'system') = (task_id IS NULL) AND (kind = 'system') = (system_what IS NOT NULL) AND (kind = 'system') = (system_ref IS NOT NULL));

CREATE FUNCTION hazem.n8n_error_hours(p_since timestamptz)
RETURNS TABLE (hour timestamptz, errors bigint)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = app, pg_temp AS $$
  SELECT date_trunc('hour', occurred_at), count(*) FROM app.workflow_errors
   WHERE occurred_at >= p_since GROUP BY 1 ORDER BY 1
$$;

GRANT SELECT ON hazem.ops_runs TO office_api, office_readonly;
REVOKE ALL ON hazem.ops_runs FROM office_app;
REVOKE INSERT, UPDATE, DELETE ON hazem.ops_runs FROM office_api;
REVOKE ALL ON FUNCTION hazem.n8n_error_hours(timestamptz) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION hazem.n8n_error_hours(timestamptz) TO office_api;

-- migrate:down
DELETE FROM hazem.panel_alerts WHERE kind = 'system';
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_system_shape;
ALTER TABLE hazem.panel_alerts DROP COLUMN system_ref, DROP COLUMN system_what;
ALTER TABLE hazem.panel_alerts ALTER COLUMN task_id SET NOT NULL;
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_kind_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_kind_check CHECK (kind IN ('task_overdue', 'task_urgent', 'task_human'));
DROP FUNCTION IF EXISTS hazem.n8n_error_hours(timestamptz);
DROP TABLE IF EXISTS hazem.ops_runs;
