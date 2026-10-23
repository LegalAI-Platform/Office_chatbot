-- 0009_panel_accounts: TASK 07a (DECISIONS v1.8, T-23). Accounts of the panel and what they did.
--   hazem.panel_users  one account per person: role (RBAC), the staff member it stands for, the
--                      password hash (ASP.NET Core Identity's PasswordHasher, never the password),
--                      the security stamp (a new one ends every session), lockout.
--   hazem.panel_audit  who did what and when in the panel. Never a password, never client text.
-- No default account: the owner creates the first manager from the terminal (docs/runbooks/panel.md).

-- migrate:up
CREATE TABLE hazem.panel_users (
  id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  username             text NOT NULL UNIQUE CHECK (username ~ '^[a-z0-9][a-z0-9._-]{2,31}$'),
  display_name         text NOT NULL CHECK (length(btrim(display_name)) BETWEEN 1 AND 120),
  email                citext NOT NULL UNIQUE CHECK (length(email) <= 254 AND email ~ '^[^@\s]+@[^@\s]+\.[^@\s]+$'),
  role                 text NOT NULL CHECK (role IN ('manager', 'lawyer', 'staff')),
  staff_id             uuid REFERENCES hazem.staff (id),
  password_hash        text NOT NULL CHECK (length(password_hash) BETWEEN 20 AND 500),
  security_stamp       text NOT NULL CHECK (length(security_stamp) BETWEEN 16 AND 100),
  must_change_password boolean NOT NULL DEFAULT true,
  failed_logins        int NOT NULL DEFAULT 0 CHECK (failed_logins BETWEEN 0 AND 100),
  lockout_end          timestamptz,
  active               boolean NOT NULL DEFAULT true,
  last_login_at        timestamptz,
  chatwoot_user_id     bigint UNIQUE,  -- TASK 07b: the Chatwoot account this panel account created
  created_at           timestamptz NOT NULL DEFAULT now(),
  updated_at           timestamptz NOT NULL DEFAULT now(),
  -- «مهامي» needs the staff member a staff account stands for.
  CONSTRAINT panel_users_staff_link CHECK (role <> 'staff' OR staff_id IS NOT NULL)
);
CREATE TRIGGER trg_panel_users_updated_at BEFORE UPDATE ON hazem.panel_users
  FOR EACH ROW EXECUTE FUNCTION app.set_updated_at();

CREATE TABLE hazem.panel_audit (
  id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  at          timestamptz NOT NULL DEFAULT now(),
  user_id     uuid REFERENCES hazem.panel_users (id),
  action      text NOT NULL CHECK (action ~ '^[a-z][a-z_.]{2,63}$'),
  target_type text CHECK (target_type IN ('user', 'task')),
  target_id   uuid,
  detail      jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(detail) = 'object')
);
CREATE INDEX panel_audit_at_idx ON hazem.panel_audit (at DESC);
CREATE INDEX panel_audit_target_idx ON hazem.panel_audit (target_type, target_id, at DESC);

-- The panel's task list: by staff member and state, then by due time.
CREATE INDEX staff_tasks_staff_state_due_idx ON hazem.staff_tasks (staff_id, state, due_at);

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.panel_users, hazem.panel_audit TO office_api;
GRANT USAGE, SELECT ON SEQUENCE hazem.panel_audit_id_seq TO office_api;
-- office_readonly reads accounts without the password hash and the security stamp.
REVOKE ALL ON hazem.panel_users FROM office_readonly;
GRANT SELECT (id, username, display_name, email, role, staff_id, must_change_password, failed_logins, lockout_end,
              active, last_login_at, chatwoot_user_id, created_at, updated_at)
  ON hazem.panel_users TO office_readonly;
GRANT SELECT ON hazem.panel_audit TO office_readonly;
REVOKE ALL ON hazem.panel_users, hazem.panel_audit FROM office_app;

-- migrate:down
DROP INDEX IF EXISTS hazem.staff_tasks_staff_state_due_idx;
DROP TABLE IF EXISTS hazem.panel_audit;
DROP TABLE IF EXISTS hazem.panel_users;
