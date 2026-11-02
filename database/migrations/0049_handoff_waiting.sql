-- migrate:up
-- DECISIONS v1.48 (the owner 2026-11-02, «نفذ»): on ***89 at 04:05 «ممكن رقم الاستاذ الشخصي» was handed to the office, the
-- written reply ended «تمام كده؟», and the next two messages got nothing (the conversation waits for staff, B-12).
-- 1. human_request says it is done (the request is with the office and someone answers here), never a question.
-- 2. handoff_waiting: the first message after the handoff, before any staff reply, gets it once; then silence again.
-- 3. no_personal_number: the lawyer's personal number is asked: not given; the services or someone from the office (buttons).
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('handoff_waiting', 1, 'رسالة حضرتك وصلت ✅ وحد من المكتب هيرد على حضرتك هنا في أقرب وقت.', 300, true,
   'DECISIONS v1.48 B-12', 'owner approval 2026-11-02'),
  ('no_personal_number', 1, 'معلش، أرقام المستشار الشخصية مش بنديها. تقدر تحجز استشارة مع المستشار سعد نفسه، أو تكلم حد من المكتب.', 300, true,
   'DECISIONS v1.48 B-12', 'owner approval 2026-11-02');

DO $$
DECLARE
  v_old hazem.approved_texts;
BEGIN
  SELECT * INTO v_old FROM hazem.approved_texts WHERE key = 'human_request' ORDER BY active DESC, version DESC LIMIT 1;
  IF FOUND AND v_old.text = 'تمام، هبلّغ المكتب إن حضرتك عايز تكلم حد.' THEN
    UPDATE hazem.approved_texts SET active = false WHERE key = 'human_request' AND active;
    INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
    VALUES ('human_request', v_old.version + 1, 'تمام، بلّغت المكتب إن حضرتك عايز تكلم حد، وحد من المكتب هيرد على حضرتك هنا.',
            v_old.max_chars, v_old.active, 'DECISIONS v1.48 B-12', 'migration 0049');
  END IF;
END $$;

-- migrate:down
DELETE FROM hazem.approved_texts WHERE key IN ('handoff_waiting', 'no_personal_number');
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM hazem.approved_texts WHERE key = 'human_request' AND updated_by = 'migration 0049') THEN
    DELETE FROM hazem.approved_texts WHERE key = 'human_request' AND updated_by = 'migration 0049';
    UPDATE hazem.approved_texts SET active = true
     WHERE key = 'human_request' AND version = (SELECT max(version) FROM hazem.approved_texts WHERE key = 'human_request');
  END IF;
END $$;
