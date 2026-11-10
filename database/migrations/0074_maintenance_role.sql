-- 0074_maintenance_role: DECISIONS v1.100. The owner (2026-11-10), before handing Hazem to the office: «انا مش عايز المكتب يعرف
-- يغير اي نصوص، انا بس اللي اقدر اغيرها … حساب الصيانة». A panel role 'maintenance', «الصيانة»: Hazem's texts and «دليل التعامل»,
-- «أدوات التجربة», and the maintenance accounts themselves are its only (the API's Permissions). No office role (manager,
-- lawyer, staff) can make one, give the role, or change, disable or take over such an account (a new password, its
-- authenticator off); the CLI on the server can. Its authenticator is always on. The owner's own account is made one on the
-- server with the CLI (set-role), never by a migration.

-- migrate:up
ALTER TABLE hazem.panel_users DROP CONSTRAINT panel_users_role_check;
ALTER TABLE hazem.panel_users ADD CONSTRAINT panel_users_role_check
  CHECK (role IN ('maintenance', 'manager', 'lawyer', 'staff'));

-- migrate:down
UPDATE hazem.panel_users SET role = 'manager' WHERE role = 'maintenance';
ALTER TABLE hazem.panel_users DROP CONSTRAINT panel_users_role_check;
ALTER TABLE hazem.panel_users ADD CONSTRAINT panel_users_role_check CHECK (role IN ('manager', 'lawyer', 'staff'));
