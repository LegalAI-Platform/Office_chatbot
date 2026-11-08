-- 0073_urf_done_past_time: DECISIONS v1.99. The owner (2026-11-08), after Ahmed Amin's chat on the test server: the
-- appointment of his Urfi contract was marked «خلص» by itself (v1.50), and the worker sent the case back to picking a time as
-- if that time had been cancelled, so that night Hazem went on with the same request and «عايز اتجوز عرفي» took the morning's
-- answers («أصلّح إن الميعاد اللي خلص يقفل القضية بدل ما يفتحها تاني؟» … «تمام نفذ»). Three changes:
--  1. hazem.urf_cases: a new state 'completed'. hazem.urf_due() closes a booked case whose appointment is marked done, with
--     that appointment (with hazem.urf_case_said on; off: exactly as v1.81). A mark corrected to «ماجاش» sends it back to
--     picking a time, as any time gone, unless another case of the same kind is already running. A time cancelled or run
--     out goes back to picking a time as before.
--  2. hazem.approved_texts situation_past, the owner's words: what Hazem answers from (and what goes when the written answer
--     is refused) when he asks about his appointment that passed, in place of «معلش، ممكن توضّحلي أكتر».
--  3. hazem.generated_replies.refused: the written tries that were refused and why, never sent, for the office to see.

-- migrate:up
ALTER TABLE hazem.urf_cases DROP CONSTRAINT urf_cases_state_check;
ALTER TABLE hazem.urf_cases ADD CONSTRAINT urf_cases_state_check CHECK (state IN (
  'collecting', 'rejected_age', 'rejected_guardian', 'redirected_consultation',
  'documents_pending', 'booked', 'lawyer_rejected', 'approved', 'abandoned', 'completed'));

CREATE OR REPLACE FUNCTION hazem.urf_due()
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v jsonb;
  v_days int;
  v_case_said boolean;
BEGIN
  -- v1.63: a case still collecting answers, or picking a time it never held, left alone for the fresh days is abandoned
  SELECT CASE WHEN value ~ '^[0-9]+$' AND value::int BETWEEN 1 AND 365 THEN value::int ELSE 3 END INTO v_days
    FROM hazem.settings WHERE key = 'intake.fresh_after_days';
  UPDATE hazem.urf_cases c SET state = 'abandoned', step = NULL
   WHERE c.state IN ('collecting', 'documents_pending') AND c.appointment_id IS NULL
     AND c.updated_at < now() - make_interval(days => COALESCE(v_days, 3));

  -- v1.99: a booked case whose appointment was marked done (by the office, or by itself after it, v1.50): the contract was
  -- made, so the case is closed with its appointment, never sent back to picking a time. With hazem.urf_case_said and
  -- understanding v2 on, as the flow reads them (HazemSettings.Flag); off: exactly as v1.81
  SELECT count(*) = 2 INTO v_case_said FROM hazem.settings
   WHERE key IN ('hazem.urf_case_said', 'hazem.understanding_v2') AND lower(value) = 'true';
  IF v_case_said THEN
    UPDATE hazem.urf_cases c SET state = 'completed'
     WHERE c.state = 'booked'
       AND EXISTS (SELECT 1 FROM hazem.appointments a WHERE a.id = c.appointment_id AND a.state = 'completed');
  END IF;

  -- v1.99: that mark corrected to «ماجاش» (v1.50: an automatic mark can be corrected): back to picking a time, as any time
  -- gone below, unless another case of the same kind is already running (one running case per kind, urf_cases_one_open)
  UPDATE hazem.urf_cases c SET state = 'documents_pending', appointment_id = NULL
   WHERE c.state = 'completed'
     AND NOT EXISTS (SELECT 1 FROM hazem.appointments a WHERE a.id = c.appointment_id AND a.state = 'completed')
     AND NOT EXISTS (SELECT 1 FROM hazem.urf_cases o WHERE o.person_id = c.person_id AND o.kind = c.kind AND o.id <> c.id
                      AND o.state IN ('collecting', 'documents_pending', 'booked'));

  -- A booked case whose time is gone (the hold ran out, the client cancelled, the office cancelled a day, he did not come)
  -- goes back to picking a time: it no longer takes files, and «عايز جواز عرفي» again offers the times.
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

INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
SELECT 'situation_past', (SELECT COALESCE(max(v.version), 0) + 1 FROM hazem.approved_texts v WHERE v.key = 'situation_past'),
       'ميعاد حضرتك ({{service}}) كان {{day}} الساعة {{time}}، وعدّى خلاص.', 300, true, 'DECISIONS v1.99 B-28',
       'owner approval 2026-11-08 (v1.99)';

ALTER TABLE hazem.generated_replies ADD COLUMN refused jsonb;
COMMENT ON COLUMN hazem.generated_replies.refused IS
  'v1.99: the written tries that were refused, [{problem, text}], the first try and its retry; never sent to the client';

-- migrate:down
ALTER TABLE hazem.generated_replies DROP COLUMN refused;

DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-08 (v1.99)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-08 (v1.99)';

CREATE OR REPLACE FUNCTION hazem.urf_due()
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v jsonb;
  v_days int;
BEGIN
  SELECT CASE WHEN value ~ '^[0-9]+$' AND value::int BETWEEN 1 AND 365 THEN value::int ELSE 3 END INTO v_days
    FROM hazem.settings WHERE key = 'intake.fresh_after_days';
  UPDATE hazem.urf_cases c SET state = 'abandoned', step = NULL
   WHERE c.state IN ('collecting', 'documents_pending') AND c.appointment_id IS NULL
     AND c.updated_at < now() - make_interval(days => COALESCE(v_days, 3));
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

-- a closed case stays closed: the state the contract went ahead with
UPDATE hazem.urf_cases SET state = 'approved' WHERE state = 'completed';
ALTER TABLE hazem.urf_cases DROP CONSTRAINT urf_cases_state_check;
ALTER TABLE hazem.urf_cases ADD CONSTRAINT urf_cases_state_check CHECK (state IN (
  'collecting', 'rejected_age', 'rejected_guardian', 'redirected_consultation',
  'documents_pending', 'booked', 'lawyer_rejected', 'approved', 'abandoned'));
