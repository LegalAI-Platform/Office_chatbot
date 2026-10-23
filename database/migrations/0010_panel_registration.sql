-- 0010_panel_registration: DECISIONS v1.9 T-23. Self-registration, codes by e-mail and the
-- authenticator app (2FA).
--   hazem.panel_users        + approval: a registered account waits ('pending', inactive, no way in)
--                              until a manager approves it and picks its role. Until then its role
--                              is a placeholder ('staff', no staff member). + the confirmed e-mail,
--                              who approved it, and the authenticator (secret encrypted by the API
--                              with PANEL_SECRET_KEY, the last time step used: a code works once).
--   hazem.panel_codes        six-digit codes sent by e-mail (confirm the address, a new password):
--                              hashed, ten minutes, five tries, used once.
--   hazem.panel_recovery_codes  one-time codes for a lost phone, hashed.
-- Accounts that existed before were made by a manager or the terminal: their e-mail counts as confirmed.

-- migrate:up
ALTER TABLE hazem.panel_users
  ADD COLUMN approval          text NOT NULL DEFAULT 'approved' CHECK (approval IN ('pending', 'approved')),
  ADD COLUMN email_verified_at timestamptz,
  ADD COLUMN approved_by       uuid REFERENCES hazem.panel_users (id) ON DELETE SET NULL,
  ADD COLUMN approved_at       timestamptz,
  ADD COLUMN totp_secret       text CHECK (totp_secret IS NULL OR length(totp_secret) BETWEEN 20 AND 300),
  ADD COLUMN totp_enabled      boolean NOT NULL DEFAULT false,
  ADD COLUMN totp_last_step    bigint,
  ADD CONSTRAINT panel_users_pending_inactive CHECK (approval = 'approved' OR NOT active),
  ADD CONSTRAINT panel_users_totp_has_secret CHECK (NOT totp_enabled OR totp_secret IS NOT NULL);

-- A pending account is nobody's staff member yet: the manager links it when approving.
ALTER TABLE hazem.panel_users DROP CONSTRAINT panel_users_staff_link;
ALTER TABLE hazem.panel_users ADD CONSTRAINT panel_users_staff_link
  CHECK (approval = 'pending' OR role <> 'staff' OR staff_id IS NOT NULL);

UPDATE hazem.panel_users SET email_verified_at = created_at;

CREATE INDEX panel_users_pending_idx ON hazem.panel_users (created_at) WHERE approval = 'pending';

CREATE TABLE hazem.panel_codes (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id    uuid NOT NULL REFERENCES hazem.panel_users (id) ON DELETE CASCADE,
  purpose    text NOT NULL CHECK (purpose IN ('verify_email', 'reset_password')),
  code_hash  text NOT NULL CHECK (length(code_hash) BETWEEN 20 AND 200),
  expires_at timestamptz NOT NULL,
  attempts   int NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 100),
  used_at    timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX panel_codes_user_idx ON hazem.panel_codes (user_id, purpose, created_at DESC);

CREATE TABLE hazem.panel_recovery_codes (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id    uuid NOT NULL REFERENCES hazem.panel_users (id) ON DELETE CASCADE,
  code_hash  text NOT NULL CHECK (length(code_hash) BETWEEN 20 AND 200),
  used_at    timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX panel_recovery_codes_user_idx ON hazem.panel_recovery_codes (user_id) WHERE used_at IS NULL;

ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_user_id_fkey;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_user_id_fkey
  FOREIGN KEY (user_id) REFERENCES hazem.panel_users (id) ON DELETE SET NULL;

GRANT SELECT, INSERT, UPDATE, DELETE ON hazem.panel_codes, hazem.panel_recovery_codes TO office_api;
REVOKE ALL ON hazem.panel_codes, hazem.panel_recovery_codes FROM office_readonly, office_app;
-- office_readonly still never reads the hash, the stamp or the authenticator secret.
GRANT SELECT (approval, email_verified_at, approved_by, approved_at, totp_enabled) ON hazem.panel_users TO office_readonly;

-- migrate:down
DELETE FROM hazem.panel_users WHERE approval = 'pending';
ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_user_id_fkey;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_user_id_fkey
  FOREIGN KEY (user_id) REFERENCES hazem.panel_users (id);
DROP TABLE IF EXISTS hazem.panel_recovery_codes;
DROP TABLE IF EXISTS hazem.panel_codes;
DROP INDEX IF EXISTS hazem.panel_users_pending_idx;
ALTER TABLE hazem.panel_users DROP CONSTRAINT panel_users_staff_link;
ALTER TABLE hazem.panel_users ADD CONSTRAINT panel_users_staff_link CHECK (role <> 'staff' OR staff_id IS NOT NULL);
ALTER TABLE hazem.panel_users
  DROP CONSTRAINT panel_users_totp_has_secret,
  DROP CONSTRAINT panel_users_pending_inactive,
  DROP COLUMN totp_last_step,
  DROP COLUMN totp_enabled,
  DROP COLUMN totp_secret,
  DROP COLUMN approved_at,
  DROP COLUMN approved_by,
  DROP COLUMN email_verified_at,
  DROP COLUMN approval;
