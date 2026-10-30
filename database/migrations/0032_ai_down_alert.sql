-- 0032_ai_down_alert: the owner (2026-10-30). The classifier's key stopped working and nobody knew: for twenty minutes
-- every message fell back to «unclear», a thanks got «مش واضحلي» and insults got «هبلّغ المكتب». Now:
--   - three messages or more in one hour that the AI could not read at all (no key, an error, a timeout, an answer it
--     could not parse; not a low confidence) make one system alert «الـ AI مش شغال»: the managers' bell and an e-mail,
--     one per hour (system_ref ai:<hour>). Counts only: no client's words or number.
--   - hazem.classifications.fallback_reason may read «backstop:<reason>»: the AI failed and code read the message
--     (an insult or a plain thanks, Backstop.cs); the intent column says what it read.

-- migrate:up
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_system_what_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_system_what_check
  CHECK (system_what IN ('backup', 'healthcheck', 'drill', 'log_scan', 'n8n', 'turn', 'ai'));

COMMENT ON COLUMN hazem.classifications.fallback_reason IS
  'Why the AI''s answer was not used (no_key, timeout, error, invalid_output, unknown_value, low_confidence); backstop:<reason> when code read the message instead (0032)';

-- migrate:down
DELETE FROM hazem.panel_alerts WHERE system_what = 'ai';
ALTER TABLE hazem.panel_alerts DROP CONSTRAINT panel_alerts_system_what_check;
ALTER TABLE hazem.panel_alerts ADD CONSTRAINT panel_alerts_system_what_check
  CHECK (system_what IN ('backup', 'healthcheck', 'drill', 'log_scan', 'n8n', 'turn'));
COMMENT ON COLUMN hazem.classifications.fallback_reason IS NULL;
