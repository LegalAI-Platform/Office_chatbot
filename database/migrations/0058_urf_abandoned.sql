-- 0058_urf_abandoned: DECISIONS v1.63. The owner (2026-11-05), on how long a client stays «the same request»: «حاسس ان المدة طويلة. وغير كده
-- موضوع العرف ده برضه حاسس ان هو كده». Two changes, both with the same number of days:
--  1. hazem.settings intake.fresh_after_days: 7 → 3 (only where it is still the old 7).
--  2. An Urfi case that is only collecting answers or waiting for a time (no appointment held) and had no activity for that many
--     days is closed as 'abandoned' by the worker (hazem.urf_due): it no longer counts as «something pending», and «عايز جواز عرفي»
--     starts from the first question instead of resuming answers from days ago. A case with a time held, booked, approved or
--     rejected is never touched: it is the office's.

-- migrate:up
ALTER TABLE hazem.urf_cases DROP CONSTRAINT urf_cases_state_check;
ALTER TABLE hazem.urf_cases ADD CONSTRAINT urf_cases_state_check CHECK (state IN (
  'collecting', 'rejected_age', 'rejected_guardian', 'redirected_consultation',
  'documents_pending', 'booked', 'lawyer_rejected', 'approved', 'abandoned'));

UPDATE hazem.settings SET value = '3', updated_by = 'migration 0058' WHERE key = 'intake.fresh_after_days' AND value = '7';

CREATE OR REPLACE FUNCTION hazem.urf_due()
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v jsonb;
  v_days int;
BEGIN
  -- v1.63: a case still collecting answers, or picking a time it never held, left alone for the fresh days is abandoned
  SELECT CASE WHEN value ~ '^[0-9]+$' AND value::int BETWEEN 1 AND 365 THEN value::int ELSE 3 END INTO v_days
    FROM hazem.settings WHERE key = 'intake.fresh_after_days';
  UPDATE hazem.urf_cases c SET state = 'abandoned', step = NULL
   WHERE c.state IN ('collecting', 'documents_pending') AND c.appointment_id IS NULL
     AND c.updated_at < now() - make_interval(days => COALESCE(v_days, 3));

  -- A booked case whose time is gone (the hold ran out, the client cancelled, the office cancelled a day) goes back to
  -- picking a time: it no longer takes files, and «عايز جواز عرفي» again offers the times.
  UPDATE hazem.urf_cases c SET state = 'documents_pending', appointment_id = NULL
   WHERE c.state = 'booked'
     AND NOT EXISTS (SELECT 1 FROM hazem.appointments a WHERE a.id = c.appointment_id AND a.state IN ('pending_payment', 'confirmed'));
  WITH due AS (
    UPDATE hazem.urf_cases c SET docs_task_at = now()
      FROM hazem.appointments a
     WHERE a.id = c.appointment_id AND c.state = 'booked' AND c.docs_task_at IS NULL
       AND a.state IN ('pending_payment', 'confirmed') AND a.starts_at <= now() + interval '24 hours' AND a.starts_at > now()
       AND NOT EXISTS (SELECT 1 FROM hazem.urf_files f WHERE f.case_id = c.id)
    RETURNING c.id, c.person_id)
  SELECT COALESCE(jsonb_agg(jsonb_build_object('id', id, 'person', person_id)), '[]'::jsonb) INTO v FROM due;
  RETURN v;
END $$;

-- migrate:down
UPDATE hazem.urf_cases SET state = 'redirected_consultation' WHERE state = 'abandoned';
ALTER TABLE hazem.urf_cases DROP CONSTRAINT urf_cases_state_check;
ALTER TABLE hazem.urf_cases ADD CONSTRAINT urf_cases_state_check CHECK (state IN (
  'collecting', 'rejected_age', 'rejected_guardian', 'redirected_consultation',
  'documents_pending', 'booked', 'lawyer_rejected', 'approved'));
UPDATE hazem.settings SET value = '7', updated_by = 'migration 0058 down' WHERE key = 'intake.fresh_after_days' AND value = '3';
CREATE OR REPLACE FUNCTION hazem.urf_due()
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v jsonb;
BEGIN
  UPDATE hazem.urf_cases c SET state = 'documents_pending', appointment_id = NULL
   WHERE c.state = 'booked'
     AND NOT EXISTS (SELECT 1 FROM hazem.appointments a WHERE a.id = c.appointment_id AND a.state IN ('pending_payment', 'confirmed'));
  WITH due AS (
    UPDATE hazem.urf_cases c SET docs_task_at = now()
      FROM hazem.appointments a
     WHERE a.id = c.appointment_id AND c.state = 'booked' AND c.docs_task_at IS NULL
       AND a.state IN ('pending_payment', 'confirmed') AND a.starts_at <= now() + interval '24 hours' AND a.starts_at > now()
       AND NOT EXISTS (SELECT 1 FROM hazem.urf_files f WHERE f.case_id = c.id)
    RETURNING c.id, c.person_id)
  SELECT COALESCE(jsonb_agg(jsonb_build_object('id', id, 'person', person_id)), '[]'::jsonb) INTO v FROM due;
  RETURN v;
END $$;
