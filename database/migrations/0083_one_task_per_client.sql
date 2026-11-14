-- migrate:up
-- DECISIONS v1.112 (the owner, 2026-11-14 10:41, of a bell full of one client's tasks: «ليه في كل النوتيفيكيشنز دي؟ … انت
-- مش حاسس ان الدنيا كده ملعبكة؟» … «ماشي تمام اعمل كدا»; one client's 23 minutes made 9 tasks, 4 of them needed):
--   hazem.staff_task_turns  a client's later messages that joined his open task of the same kind: one task for one job, with
--                           all his words on it (the code joins them; the office's missing detail and the paid urgent call
--                           keep their own one-task rules)
--   staff_tasks.joined_at   the last time a message joined it: the bell tells the task again as new
--   staff_tasks.type        gains «attached_message»: words written with a photo, a file or a press that nothing else of the
--                           turn took to the office (they were «unclear_message», which told the office they were not clear)
--   hazem.urf_due()         the Urfi documents' task waits for the time to be paid and an hour after the receipt (Hazem asks
--                           for the documents with it): it came 7 seconds after the hold of a time the same day, before the
--                           client paid or was asked for anything

CREATE TABLE hazem.staff_task_turns (
  task_id  uuid NOT NULL REFERENCES hazem.staff_tasks (id) ON DELETE CASCADE,
  turn_id  text NOT NULL,
  added_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (task_id, turn_id)
);
GRANT SELECT, INSERT, DELETE ON hazem.staff_task_turns TO office_api;
GRANT SELECT ON hazem.staff_task_turns TO office_readonly;

ALTER TABLE hazem.staff_tasks ADD COLUMN joined_at timestamptz;

ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help',
  'reminder_failed', 'office_info_missing',
  'service_question', 'payment_review_due',
  'rating_low', 'urgent_call_failed', 'urgent_call_unanswered',
  'appointment_unmarked', 'urgent_call',
  'attached_message'));

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
       -- v1.112: a time paid for (Hazem asks for the documents with the receipt), an hour after it, never a time only held
       AND a.state = 'confirmed' AND a.starts_at <= now() + interval '24 hours' AND a.starts_at > now()
       AND EXISTS (SELECT 1 FROM hazem.payments p WHERE p.appointment_id = a.id
                    AND COALESCE(p.proof_at, p.reviewed_at) <= now() - interval '1 hour')
       AND NOT EXISTS (SELECT 1 FROM hazem.urf_files f WHERE f.case_id = c.id)
    RETURNING c.id, c.person_id)
  SELECT COALESCE(jsonb_agg(jsonb_build_object('id', id, 'person', person_id)), '[]'::jsonb) INTO v FROM due;
  RETURN v;
END $$;

-- migrate:down
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

UPDATE hazem.staff_tasks SET type = 'unclear_message' WHERE type = 'attached_message'
   AND NOT EXISTS (SELECT 1 FROM hazem.staff_tasks u WHERE u.turn_id = staff_tasks.turn_id AND u.type = 'unclear_message');
DELETE FROM hazem.staff_tasks WHERE type = 'attached_message';
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help',
  'reminder_failed', 'office_info_missing',
  'service_question', 'payment_review_due',
  'rating_low', 'urgent_call_failed', 'urgent_call_unanswered',
  'appointment_unmarked', 'urgent_call'));

ALTER TABLE hazem.staff_tasks DROP COLUMN joined_at;
DROP TABLE hazem.staff_task_turns;
