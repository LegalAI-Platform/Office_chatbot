-- migrate:up
-- DECISIONS v1.111 (the owner, 2026-11-14: he cancelled a client's Urfi appointment from «المواعيد» and the client stayed in
-- «بانتظار إجرائك»: «لما انا لغيت الحجز … تشالش ليه» … «Yes, add it»). An appointment cancelled (by the office or the client)
-- or whose hold ran out takes with it the office's tasks that were made for it alone, each keyed by its id:
--   payment_review_due     booking:<id>:review          its transfer not reviewed while it came near
--   appointment_unmarked   booking:<id>:unmarked        it passed and nobody marked it
--   calendar_conflict      booking:<id>:conflict:<time> the lawyer's calendar busy at its time
--   reminder_failed        reminder:<id>:<kind>:<time>  a reminder of it that could not go
--   urf_documents_missing  urf:<case>:documents         its Urfi case's documents not come yet
-- They become «cancelled» (their alerts read, as any finished task's: v1.110), and the panel shows it within seconds (the
-- tasks' rows changed: v1.106). Every other task stays: a message not understood, a refund to make, a message to the client
-- that failed, the office's own follow-up of the cancellation. A receipt stays in «الإيصالات» to be reviewed.
-- The Urfi documents' task is the case's (made once: docs_task_at), not the time's: closed, it keeps the time it was for
-- in its key, and the case may have it again for the next time it books (its own 24 hours).

CREATE FUNCTION hazem.close_appointment_tasks(p_id uuid, p_person uuid)
RETURNS int LANGUAGE plpgsql SECURITY DEFINER SET search_path = hazem, pg_temp AS $$
DECLARE
  k text := replace(p_id::text, '-', '');
  n int;
  m int;
BEGIN
  UPDATE hazem.staff_tasks t
     SET state = 'cancelled'
   WHERE t.person_id = p_person
     AND t.state IN ('open', 'in_progress', 'overdue')
     AND (   (t.type = 'payment_review_due' AND t.turn_id = 'booking:' || k || ':review')
          OR (t.type = 'appointment_unmarked' AND t.turn_id = 'booking:' || k || ':unmarked')
          OR (t.type = 'calendar_conflict' AND t.turn_id LIKE 'booking:' || k || ':conflict:%')
          OR (t.type = 'reminder_failed' AND t.turn_id LIKE 'reminder:' || k || ':%'));
  GET DIAGNOSTICS n = ROW_COUNT;

  UPDATE hazem.staff_tasks t
     SET state = CASE WHEN t.state IN ('open', 'in_progress', 'overdue') THEN 'cancelled' ELSE t.state END,
         turn_id = t.turn_id || ':' || k -- done by the office already: kept as it is, its key freed all the same
    FROM hazem.urf_cases c
   WHERE c.appointment_id = p_id
     AND t.person_id = p_person
     AND t.type = 'urf_documents_missing'
     AND t.turn_id = 'urf:' || replace(c.id::text, '-', '') || ':documents';
  GET DIAGNOSTICS m = ROW_COUNT;
  UPDATE hazem.urf_cases SET docs_task_at = NULL WHERE appointment_id = p_id AND docs_task_at IS NOT NULL;
  RETURN n + m;
END
$$;

CREATE FUNCTION hazem.appointment_closed()
RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path = hazem, pg_temp AS $$
BEGIN
  PERFORM hazem.close_appointment_tasks(NEW.id, NEW.person_id);
  RETURN NULL;
END
$$;

-- called by the trigger only (as its owner); a trigger function cannot be called by itself
REVOKE ALL ON FUNCTION hazem.close_appointment_tasks(uuid, uuid) FROM PUBLIC;

CREATE TRIGGER trg_appointment_closed AFTER UPDATE OF state ON hazem.appointments
  FOR EACH ROW
  WHEN (OLD.state IN ('confirmed', 'pending_payment') AND NEW.state IN ('cancelled_by_office', 'cancelled_by_client', 'expired'))
  EXECUTE FUNCTION hazem.appointment_closed();

-- the ones still open for appointments cancelled or run out before this
SELECT hazem.close_appointment_tasks(a.id, a.person_id)
  FROM hazem.appointments a
 WHERE a.state IN ('cancelled_by_office', 'cancelled_by_client', 'expired');

-- migrate:down
DROP TRIGGER IF EXISTS trg_appointment_closed ON hazem.appointments;
DROP FUNCTION IF EXISTS hazem.appointment_closed();
DROP FUNCTION IF EXISTS hazem.close_appointment_tasks(uuid, uuid);
