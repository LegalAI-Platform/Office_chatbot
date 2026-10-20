#!/bin/bash
# Creates the n8n and office databases and their least-privilege roles.
# office_api (the .NET backend, DECISIONS v1.3) only gets CONNECT here; its schema "hazem"
# and the grants on it come from migration 0003.
# Runs automatically on the first start (empty volume). Safe to run again:
#   docker compose exec postgres bash /docker-entrypoint-initdb.d/01-create-databases-and-roles.sh
set -euo pipefail

: "${N8N_DB_PASSWORD:?missing}"
: "${OFFICE_MIGRATOR_PASSWORD:?missing}"
: "${OFFICE_APP_PASSWORD:?missing}"
: "${OFFICE_READONLY_PASSWORD:?missing}"
: "${OFFICE_API_PASSWORD:?missing}"

run_psql() {
  psql -v ON_ERROR_STOP=1 --no-psqlrc --username "$POSTGRES_USER" \
    -v n8n_pw="$N8N_DB_PASSWORD" \
    -v migrator_pw="$OFFICE_MIGRATOR_PASSWORD" \
    -v app_pw="$OFFICE_APP_PASSWORD" \
    -v readonly_pw="$OFFICE_READONLY_PASSWORD" \
    -v api_pw="$OFFICE_API_PASSWORD" \
    "$@"
}

# --- Roles and databases (connected to the maintenance database) ---
run_psql --dbname postgres <<'EOSQL'
-- Roles: created if missing, password always re-synced from the environment.
SELECT 'CREATE ROLE n8n_user LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'n8n_user') \gexec
SELECT 'CREATE ROLE office_migrator LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'office_migrator') \gexec
SELECT 'CREATE ROLE office_app LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'office_app') \gexec
SELECT 'CREATE ROLE office_readonly LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'office_readonly') \gexec
SELECT 'CREATE ROLE office_api LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'office_api') \gexec

ALTER ROLE n8n_user        WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'n8n_pw';
ALTER ROLE office_migrator WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'migrator_pw';
ALTER ROLE office_app      WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'app_pw';
ALTER ROLE office_readonly WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'readonly_pw';
ALTER ROLE office_api      WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'api_pw';

-- Application roles look in the "app" schema first (created by migration 0001).
ALTER ROLE office_app      SET search_path = app, public;
ALTER ROLE office_readonly SET search_path = app, public;
ALTER ROLE office_app      SET timezone = 'Africa/Cairo';
ALTER ROLE office_readonly SET timezone = 'Africa/Cairo';
-- The .NET backend works in schema "hazem" (migration 0003) and has no access to "app".
ALTER ROLE office_api      SET search_path = hazem, public;
ALTER ROLE office_api      SET timezone = 'Africa/Cairo';

-- Databases
SELECT 'CREATE DATABASE n8n OWNER n8n_user' WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'n8n') \gexec
SELECT 'CREATE DATABASE office OWNER office_migrator' WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'office') \gexec

ALTER DATABASE n8n    OWNER TO n8n_user;
ALTER DATABASE office OWNER TO office_migrator;

-- Nobody connects to a database unless explicitly granted.
REVOKE ALL ON DATABASE n8n    FROM PUBLIC;
REVOKE ALL ON DATABASE office FROM PUBLIC;
REVOKE ALL ON DATABASE postgres FROM PUBLIC;

GRANT CONNECT ON DATABASE office TO office_app, office_readonly, office_api;
-- Owners (n8n_user on n8n, office_migrator on office) keep their implicit rights.
EOSQL

# --- Privileges inside the office database ---
run_psql --dbname office <<'EOSQL'
-- The public schema stays closed to application roles.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Tables and sequences created later by office_migrator are granted automatically.
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO office_app;
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator
  GRANT USAGE, SELECT ON SEQUENCES TO office_app;
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator
  GRANT SELECT ON TABLES TO office_readonly;
ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator
  GRANT SELECT ON SEQUENCES TO office_readonly;
EOSQL

echo "[init] databases n8n and office, and roles n8n_user, office_migrator, office_app, office_readonly, office_api are ready."
