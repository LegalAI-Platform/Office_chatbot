-- 0075_panel_tour: the panel's first-sign-in tour (v1.102). The owner (2026-11-12): bubbles over the real panel, step by step,
-- for whoever opens it for the first time; «دلوقتي هنفرض كل الناس إن هم دخلوا أول مرة للسيستم». So every account,
-- the ones that already exist too, starts without it (NULL) and sees the tour once.
--   hazem.panel_users  + tour_seen_at: when the account skipped or finished the tour. Set once by the account itself
--                        (POST /api/panel/account/tour-seen); a replay from «اعرض الجولة تاني» changes nothing here.
-- office_api already has every column of the table (0009). office_readonly sees it too (it is no secret).

-- migrate:up
ALTER TABLE hazem.panel_users ADD COLUMN tour_seen_at timestamptz;

GRANT SELECT (tour_seen_at) ON hazem.panel_users TO office_readonly;

-- migrate:down
ALTER TABLE hazem.panel_users DROP COLUMN tour_seen_at;
