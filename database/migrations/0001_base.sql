-- 0001_base: foundations only. No business tables (each later task adds its own).

-- migrate:up
CREATE EXTENSION IF NOT EXISTS pgcrypto;   -- gen_random_uuid(), field encryption later
CREATE EXTENSION IF NOT EXISTS citext;     -- case-insensitive text

CREATE SCHEMA IF NOT EXISTS app AUTHORIZATION office_migrator;
GRANT USAGE ON SCHEMA app TO office_app, office_readonly;

-- Generic trigger: keeps updated_at current on every UPDATE.
--   CREATE TRIGGER trg_<table>_updated_at BEFORE UPDATE ON app.<table>
--     FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();
CREATE OR REPLACE FUNCTION app.set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  NEW.updated_at := now();
  RETURN NEW;
END;
$$;

-- migrate:down
DROP FUNCTION IF EXISTS app.set_updated_at();
DROP SCHEMA IF EXISTS app;
DROP EXTENSION IF EXISTS citext;
DROP EXTENSION IF EXISTS pgcrypto;
