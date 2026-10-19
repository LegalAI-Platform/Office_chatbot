#!/bin/bash
# Creates the office database and its least-privilege roles (DECISIONS v1.1, T-04).
# Runs automatically on the first start (empty volume). Safe to run again:
#   docker compose exec postgres bash /docker-entrypoint-initdb.d/01-create-databases-and-roles.sh
set -euo pipefail

: "${OFFICE_MIGRATOR_PASSWORD:?missing}"
: "${OFFICE_APP_PASSWORD:?missing}"
: "${OFFICE_READONLY_PASSWORD:?missing}"

run_psql() {
  psql -v ON_ERROR_STOP=1 --no-psqlrc --username "$POSTGRES_USER" \
    -v migrator_pw="$OFFICE_MIGRATOR_PASSWORD" \
    -v app_pw="$OFFICE_APP_PASSWORD" \
    -v readonly_pw="$OFFICE_READONLY_PASSWORD" \
    "$@"
}

# --- Roles and database (connected to the maintenance database) ---
run_psql --dbname postgres <<'EOSQL'
-- Roles: created if missing, password always re-synced from the environment.
SELECT 'CREATE ROLE office_migrator LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'office_migrator') \gexec
SELECT 'CREATE ROLE office_app LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'office_app') \gexec
SELECT 'CREATE ROLE office_readonly LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'office_readonly') \gexec

ALTER ROLE office_migrator WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'migrator_pw';
ALTER ROLE office_app      WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'app_pw';
ALTER ROLE office_readonly WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'readonly_pw';

-- Application roles look in the "app" schema first (created by the first EF Core migration).
ALTER ROLE office_app      SET search_path = app, public;
ALTER ROLE office_readonly SET search_path = app, public;
ALTER ROLE office_app      SET timezone = 'Africa/Cairo';
ALTER ROLE office_readonly SET timezone = 'Africa/Cairo';

SELECT 'CREATE DATABASE office OWNER office_migrator' WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'office') \gexec
ALTER DATABASE office OWNER TO office_migrator;

-- Nobody connects to a database unless explicitly granted.
REVOKE ALL ON DATABASE office FROM PUBLIC;
REVOKE ALL ON DATABASE postgres FROM PUBLIC;
GRANT CONNECT ON DATABASE office TO office_app, office_readonly;
-- office_migrator owns office and keeps its implicit rights.
EOSQL

# --- Privileges inside the office database ---
run_psql --dbname office <<'EOSQL'
-- The public schema stays closed to application roles.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Tables and sequences created later by office_migrator are granted automatically.
-- (The first EF migration repeats this per schema, so new test databases work too.)
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO office_app;
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator
  GRANT USAGE, SELECT ON SEQUENCES TO office_app;
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator
  GRANT SELECT ON TABLES TO office_readonly;
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator
  GRANT SELECT ON SEQUENCES TO office_readonly;
EOSQL

echo "[init] database office and roles office_migrator, office_app, office_readonly are ready."
