-- 0036_urgent_alert_in_window: v1.34 (B-09.6), for testing only (the owner, 2026-10-31, «نفذ دي شباك الـ 24 ساعة»).
--   Until Meta approves the template urgent_alert, the lawyer's alert cannot reach WhatsApp. With urgent.alert_in_window
--   on, the alert to the lawyer or the manager goes first as a normal message with the same words and «شوف التحويل» when
--   that number wrote to the bot in the last 24 hours (Meta allows a normal message then), and as the template otherwise.
--   Off by default. Switched from the panel's «أدوات التجربة» only (the test environment), and the API ignores it anywhere
--   else; the healthcheck fails if it is on while Hazem answers everyone.

-- migrate:up
INSERT INTO hazem.settings (key, value, description) VALUES
  ('urgent.alert_in_window', 'false',
   'v1.34 (B-09.6), TEST ONLY: the urgent alert as a normal message when the lawyer or manager wrote in the last 24 hours')
ON CONFLICT (key) DO NOTHING;

-- migrate:down
DELETE FROM hazem.panel_audit WHERE action = 'testing.urgent_window';
DELETE FROM hazem.settings WHERE key = 'urgent.alert_in_window';
