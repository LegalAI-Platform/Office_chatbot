-- migrate:up
-- DECISIONS v1.49 (the owner 2026-11-03, «نفذ», the texts approved in chat):
-- 1. The consultation offer says, once a day, that there is an urgent call: «خلال دقايق لو فاضي» while the panel's hours are
--    open, «أول ما مواعيد العمل تبدأ الساعة …» before a morning opening; and the urgent call is a third button.
-- 2. A normal phone call from the office's number is a third app beside WhatsApp and FaceTime (Egyptian or hidden numbers).
ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_call_app_check;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_call_app_check CHECK (call_app IN ('whatsapp', 'facetime', 'meet', 'phone'));

INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('consultation_offer_urgent_day', 1, 'المستشار يقدر يشوف الموضوع مع حضرتك في استشارة: بالتليفون بـ{{price_phone}} أو في المكتب بـ{{price_office}}. ولو الموضوع مستعجل، فيه استشارة عاجلة بالتليفون بـ{{price_urgent}}، والمستشار بيكلم حضرتك خلال دقايق لو فاضي.',
   300, true, 'DECISIONS v1.49 B-23', 'owner approval 2026-11-03'),
  ('consultation_offer_urgent_night', 1, 'المستشار يقدر يشوف الموضوع مع حضرتك في استشارة: بالتليفون بـ{{price_phone}} أو في المكتب بـ{{price_office}}. ولو الموضوع مستعجل، فيه استشارة عاجلة بالتليفون بـ{{price_urgent}}، والمستشار بيكلم حضرتك أول ما مواعيد العمل تبدأ الساعة {{opens}} الصبح.',
   300, true, 'DECISIONS v1.49 B-23', 'owner approval 2026-11-03'),
  ('btn_book_urgent', 1, '🚨 استشارة عاجلة', 20, true, 'DECISIONS v1.49 B-23', 'owner approval 2026-11-03'),
  ('ask_call_app_phone', 1, 'تمام يا فندم. المستشار هيكلم حضرتك في ميعادك على واتساب، أو FaceTime لو معاك آيفون، أو مكالمة تليفون عادية من رقم المكتب. تحب المكالمة تبقى على إيه؟',
   300, true, 'DECISIONS v1.49 B-23', 'owner approval 2026-11-03'),
  ('btn_app_phone', 1, '📞 مكالمة عادية', 20, true, 'DECISIONS v1.49 B-23', 'owner approval 2026-11-03'),
  ('btn_apps_list', 1, 'اختار', 20, true, 'DECISIONS v1.49 B-23', 'migration 0050');

-- «حقايق المكتب»: the normal call too, where the office has not reworded the text since (as 0018)
DO $$
DECLARE
  v_old hazem.approved_texts;
BEGIN
  SELECT * INTO v_old FROM hazem.approved_texts WHERE key = 'office_facts' ORDER BY active DESC, version DESC LIMIT 1;
  IF FOUND AND position('على واتساب على رقم العميل نفسه، أو FaceTime لو معاه آيفون.' IN v_old.text) > 0 THEN
    UPDATE hazem.approved_texts SET active = false WHERE key = 'office_facts' AND active;
    INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
    VALUES ('office_facts', v_old.version + 1,
            replace(v_old.text, 'على واتساب على رقم العميل نفسه، أو FaceTime لو معاه آيفون.',
                    'من رقم المكتب على واتساب أو مكالمة عادية، أو FaceTime لو معاه آيفون.'),
            v_old.max_chars, v_old.active, v_old.source, 'migration 0050');
  END IF;
END $$;

-- migrate:down
DELETE FROM hazem.approved_texts WHERE key IN ('consultation_offer_urgent_day', 'consultation_offer_urgent_night', 'btn_book_urgent',
  'ask_call_app_phone', 'btn_app_phone', 'btn_apps_list');
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM hazem.approved_texts WHERE key = 'office_facts' AND updated_by = 'migration 0050') THEN
    DELETE FROM hazem.approved_texts WHERE key = 'office_facts' AND updated_by = 'migration 0050';
    UPDATE hazem.approved_texts SET active = true
     WHERE key = 'office_facts' AND version = (SELECT max(version) FROM hazem.approved_texts WHERE key = 'office_facts');
  END IF;
END $$;
UPDATE hazem.appointments SET call_app = 'whatsapp' WHERE call_app = 'phone';
ALTER TABLE hazem.appointments DROP CONSTRAINT appointments_call_app_check;
ALTER TABLE hazem.appointments ADD CONSTRAINT appointments_call_app_check CHECK (call_app IN ('whatsapp', 'facetime', 'meet'));
