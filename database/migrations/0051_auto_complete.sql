-- migrate:up
-- DECISIONS v1.50 (TASK 19, the owner 2026-11-03: «نفذ TASK 19»): a passed appointment nobody marked is marked «خلص» by itself.
-- A confirmed appointment whose end is booking.auto_complete_hours ago (3; 0 = off) and whose transfer the office has
-- verified (or that has no payment) becomes completed with completed_by = 'auto'. One still under review gets the office
-- task appointment_unmarked instead (the worker, once). A move shifts the same row's times, so the hours count from the new
-- time. completed_by says who marked it (staff, from the panel, or auto); an automatic mark can be corrected until its rating
-- has gone out.
ALTER TABLE hazem.appointments ADD COLUMN completed_by text CHECK (completed_by IN ('staff', 'auto'));
UPDATE hazem.appointments SET completed_by = 'staff' WHERE state = 'completed';

INSERT INTO hazem.settings (key, value, description) VALUES
  ('booking.auto_complete_hours', '3', 'v1.50: hours after an appointment ends before nobody''s mark becomes «خلص (تلقائي)»; 0 = off')
ON CONFLICT (key) DO NOTHING;

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
  'appointment_unmarked'));

-- The due appointments marked at once (twice in a row marks nothing new); their ids come back for the log.
CREATE FUNCTION hazem.auto_complete_appointments(p_now timestamptz, p_hours int) RETURNS SETOF uuid
LANGUAGE sql SET search_path = hazem, pg_temp AS $$
  UPDATE hazem.appointments a
     SET state = 'completed', completed_at = p_now, completed_by = 'auto', updated_at = p_now
   WHERE a.state = 'confirmed'
     AND p_hours > 0
     AND a.ends_at <= p_now - make_interval(hours => p_hours)
     AND NOT EXISTS (SELECT 1 FROM hazem.payments p WHERE p.appointment_id = a.id AND p.state <> 'verified')
  RETURNING a.id
$$;

GRANT EXECUTE ON FUNCTION hazem.auto_complete_appointments(timestamptz, int) TO office_api;

-- migrate:down
DROP FUNCTION hazem.auto_complete_appointments(timestamptz, int);
DELETE FROM hazem.panel_alerts WHERE task_id IN (SELECT id FROM hazem.staff_tasks WHERE type = 'appointment_unmarked');
DELETE FROM hazem.staff_tasks WHERE type = 'appointment_unmarked';
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
  'rating_low', 'urgent_call_failed', 'urgent_call_unanswered'));
DELETE FROM hazem.settings WHERE key = 'booking.auto_complete_hours';
ALTER TABLE hazem.appointments DROP COLUMN completed_by;
