-- 0018_prices_faq: TASK 06 (DECISIONS v1.17, B-20 / B-23).
--   The prices of B-23 become settings (price.*), edited from the panel by the lawyer and the manager (prices.manage). A new
--   price counts for new requests only: an appointment, a payment, a service order keep the amount they were made with.
--   The SQL rules read them through hazem.price(); the ten texts that quote a price get a new version with {{price_*}} slots,
--   filled from the same settings when the texts are read, so Hazem never says one price and asks for another.
--   B-20 (1.17): the office's questions (address, hours, booking, payment, refund, prices, the fee policy, the practice areas)
--   are answered from approved texts filled from the panel: a new intent office_info with info_topic.

-- migrate:up
INSERT INTO hazem.settings (key, value, description) VALUES
  ('price.phone_video', '500', 'B-23 (1.17): a consultation by phone or video (15 minutes)'),
  ('price.office', '1000', 'B-23 (1.17): a meeting at the office (30 minutes)'),
  ('price.urgent', '1000', 'B-23 (1.17): an urgent consultation by phone (15 minutes)'),
  ('price.review_min', '1500', 'B-23 (1.17): reviewing documents or a contract, from'),
  ('price.review_max', '3000', 'B-23 (1.17): reviewing documents or a contract, up to'),
  ('price.drafting_from', '3000', 'B-23 (1.17): drafting a contract, from'),
  ('price.formal_notice', '2000', 'B-23 (1.17): a formal notice'),
  ('price.written_opinion', '2000', 'B-23 (1.17): a written opinion, on top of the consultation (B-17)'),
  ('price.urf_office_witnesses', '5000', 'B-23 (1.17): an Urfi contract or divorce with the office''s witnesses'),
  ('price.urf_own_witnesses', '4000', 'B-23 (1.17): an Urfi contract or divorce with the client''s witnesses')
ON CONFLICT (key) DO NOTHING;

-- A price the panel set, or B-23's when it is missing or not a positive whole number.
CREATE FUNCTION hazem.price(p_key text, p_default int)
RETURNS int LANGUAGE sql STABLE SET search_path = hazem, pg_temp AS $$
  SELECT COALESCE((SELECT value::int FROM hazem.settings WHERE key = p_key AND value ~ '^[1-9][0-9]{0,6}$'), p_default)
$$;

-- B-16.3 / B-17 (1.17): as in 0015, the ranges and the 2,000 from the price list.
CREATE OR REPLACE FUNCTION hazem.price_service_order(p_order uuid, p_price int, p_exception boolean, p_due_on date, p_delivery text, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.service_orders;
  v_in_range boolean;
BEGIN
  SELECT * INTO v FROM hazem.service_orders WHERE id = p_order FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v.state NOT IN ('collecting', 'awaiting_price') THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  IF p_price IS NULL OR p_price <= 0 OR p_price > 1000000 OR p_due_on IS NULL OR p_due_on < current_date
     OR p_delivery NOT IN ('file', 'call') THEN
    RETURN jsonb_build_object('status', 'invalid');
  END IF;
  v_in_range := CASE v.kind
                  WHEN 'document_review' THEN p_price BETWEEN hazem.price('price.review_min', 1500) AND hazem.price('price.review_max', 3000)
                  WHEN 'contract_drafting' THEN p_price >= hazem.price('price.drafting_from', 3000)
                  WHEN 'formal_notice' THEN p_price = hazem.price('price.formal_notice', 2000)
                  ELSE p_price = hazem.price('price.written_opinion', 2000) END;
  IF NOT v_in_range AND NOT p_exception THEN
    RETURN jsonb_build_object('status', 'out_of_range');
  END IF;

  UPDATE hazem.service_orders
     SET state = 'awaiting_payment', price = p_price, price_exception = NOT v_in_range, due_on = p_due_on, delivery = p_delivery,
         priced_by = p_by, priced_at = now(), price_until = now() + interval '3 days', client_notice = 'pending', notice_kind = 'priced'
   WHERE id = v.id;
  INSERT INTO hazem.payments (service_order_id, amount_due) VALUES (v.id, p_price);
  RETURN jsonb_build_object('status', 'priced');
END $$;

CREATE OR REPLACE FUNCTION hazem.open_written_opinion(p_appointment uuid, p_due_on date, p_delivery text, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_a hazem.appointments;
  v_id uuid;
  v_price int := hazem.price('price.written_opinion', 2000);
BEGIN
  SELECT * INTO v_a FROM hazem.appointments WHERE id = p_appointment;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v_a.state <> 'completed' THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  IF p_due_on IS NULL OR p_due_on < current_date OR p_delivery NOT IN ('file', 'call') THEN
    RETURN jsonb_build_object('status', 'invalid');
  END IF;
  IF EXISTS (SELECT 1 FROM hazem.service_orders WHERE opened_key = 'opinion:' || p_appointment)
     OR EXISTS (SELECT 1 FROM hazem.service_orders WHERE person_id = v_a.person_id AND kind = 'written_opinion' AND state NOT IN ('delivered', 'closed')) THEN
    RETURN jsonb_build_object('status', 'exists');
  END IF;
  INSERT INTO hazem.service_orders (person_id, intake_id, kind, state, appointment_id, opened_key, price, due_on, delivery,
                                    priced_by, priced_at, price_until, client_notice, notice_kind)
  VALUES (v_a.person_id, v_a.intake_id, 'written_opinion', 'awaiting_payment', v_a.id, 'opinion:' || p_appointment, v_price, p_due_on, p_delivery,
          p_by, now(), now() + interval '3 days', 'pending', 'priced')
  RETURNING id INTO v_id;
  INSERT INTO hazem.payments (service_order_id, amount_due) VALUES (v_id, v_price);
  RETURN jsonb_build_object('status', 'opened', 'id', v_id);
END $$;

-- The ten texts that quote B-23: a new version with the price slots, the same words. Only where the office has not reworded
-- the text since (its newest version still says the old words); the new version takes the old one's place and source.
DO $$
DECLARE
  r record;
  v_old hazem.approved_texts;
BEGIN
  FOR r IN SELECT * FROM (VALUES
    ('consultation_offer', 'الأستاذ يقدر يشوف الموضوع مع حضرتك في استشارة: بالتليفون بـ500 أو في المكتب بـ1,000.',
                           'الأستاذ يقدر يشوف الموضوع مع حضرتك في استشارة: بالتليفون بـ{{price_phone}} أو في المكتب بـ{{price_office}}.'),
    ('urgent_price', 'الاستشارة العاجلة بالتليفون ربع ساعة بـ1,000 جنيه.', 'الاستشارة العاجلة بالتليفون ربع ساعة بـ{{price_urgent}} جنيه.'),
    ('document_review_request', 'تحت أمرك. مراجعة العقد بتبدأ من 1,500 جنيه. ابعتلي صورة العقد هنا.',
                                'تحت أمرك. مراجعة العقد بتبدأ من {{price_review_min}} جنيه. ابعتلي صورة العقد هنا.'),
    ('written_opinion', 'الرأي المكتوب بيكون بعد الاستشارة، وسعره سعر الاستشارة + 2,000 جنيه.',
                        'الرأي المكتوب بيكون بعد الاستشارة، وسعره سعر الاستشارة + {{price_opinion}} جنيه.'),
    ('contract_drafting_request', 'صياغة العقد بتبدأ من 3,000 جنيه حسب نوعه. ابعتلي أي تفاصيل أو ورق عندك.',
                                  'صياغة العقد بتبدأ من {{price_drafting_from}} جنيه حسب نوعه. ابعتلي أي تفاصيل أو ورق عندك.'),
    ('formal_notice_request', 'الإنذار الرسمي بـ2,000 جنيه. ابعتلي الورق اللي عندك.', 'الإنذار الرسمي بـ{{price_notice}} جنيه. ابعتلي الورق اللي عندك.'),
    ('price_document_review', 'مراجعة المستندات أو العقد من 1,500 لحد 3,000 جنيه، والأستاذ بيحدد السعر قبل الدفع.',
                              'مراجعة المستندات أو العقد من {{price_review_min}} لحد {{price_review_max}} جنيه، والأستاذ بيحدد السعر قبل الدفع.'),
    ('price_contract_drafting', 'صياغة العقد بتبدأ من 3,000 جنيه، والأستاذ بيحدد السعر النهائي حسب نوع العقد.',
                                'صياغة العقد بتبدأ من {{price_drafting_from}} جنيه، والأستاذ بيحدد السعر النهائي حسب نوع العقد.'),
    ('price_formal_notice', 'الإنذار الرسمي بـ2,000 جنيه.', 'الإنذار الرسمي بـ{{price_notice}} جنيه.'),
    ('price_urf', 'الجواز أو الطلاق العرفي بـ5,000 جنيه لو الشهود من المكتب، و4,000 لو الشهود من طرف حضرتك.',
                  'الجواز أو الطلاق العرفي بـ{{price_urf_office}} جنيه لو الشهود من المكتب، و{{price_urf_own}} لو الشهود من طرف حضرتك.')
  ) AS t(key, old_text, new_text)
  LOOP
    SELECT * INTO v_old FROM hazem.approved_texts WHERE key = r.key ORDER BY active DESC, version DESC LIMIT 1;
    CONTINUE WHEN NOT FOUND OR v_old.text <> r.old_text;
    UPDATE hazem.approved_texts SET active = false WHERE key = r.key AND active;
    INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
    VALUES (r.key, (SELECT max(version) + 1 FROM hazem.approved_texts WHERE key = r.key), r.new_text, v_old.max_chars, v_old.active,
            v_old.source, 'migration 0018');
  END LOOP;
END $$;

-- B-20 (1.17): the answers to the office's questions. Proposed in TASK 06 and approved by the owner the same day
-- («موافق على نصوص ردود أسئلة المكتب الجديدة»). Without them such a question gets the consultation offer.
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('office_address', 1, '📍 عنوان المكتب: {{address}}.
الخريطة: {{map}}', 300, true, 'proposal TASK 06', 'owner approval 2026-10-26'),
  ('office_hours', 1, '🕐 مواعيد الاستشارات: {{hours}}.', 300, true, 'proposal TASK 06', 'owner approval 2026-10-26'),
  ('office_how_to_book', 1, 'الحجز من هنا على واتساب: بتختار نوع الاستشارة والميعاد، وبعدها بتحوّل المبلغ وتبعت صورة التحويل هنا، والميعاد يتأكد.', 300, true, 'proposal TASK 06', 'owner approval 2026-10-26'),
  ('office_how_to_pay', 1, 'الدفع مقدم على InstaPay: {{instapay}}، أو محفظة كاش: {{wallet}}.
بعد التحويل ابعت صورة (سكرين شوت) التحويل هنا في الشات.', 300, true, 'proposal TASK 06', 'owner approval 2026-10-26'),
  ('office_refund', 1, 'لو حضرتك لغيت خلال 24 ساعة من الدفع وقبل الميعاد، المبلغ بيرجع كامل، وبعد كده مفيش استرداد. ولو المكتب هو اللي لغى، بتختار ميعاد تاني أو استرداد كامل.', 300, true, 'proposal TASK 06', 'owner approval 2026-10-26'),
  ('office_prices', 1, '💼 الأسعار:
• استشارة بالتليفون أو الفيديو (ربع ساعة): {{price_phone}} جنيه
• مقابلة في المكتب (نص ساعة): {{price_office}} جنيه
• استشارة عاجلة بالتليفون: {{price_urgent}} جنيه
• مراجعة عقد أو مستندات: من {{price_review_min}} لـ {{price_review_max}} جنيه
• صياغة عقد: من {{price_drafting_from}} جنيه
• إنذار رسمي: {{price_notice}} جنيه
• رأي مكتوب: الاستشارة + {{price_opinion}} جنيه
• جواز أو طلاق عرفي: {{price_urf_office}} جنيه (شهود المكتب) أو {{price_urf_own}} (شهود حضرتك)
وأتعاب القضايا بتتحدد بعد الاستشارة.', 600, true, 'proposal TASK 06', 'owner approval 2026-10-26'),
  ('office_fee_policy', 1, 'مبلغ الاستشارة بيتخصم من أتعاب القضية لو المكتب مسك القضية بعدها.', 300, true, 'proposal TASK 06', 'owner approval 2026-10-26'),
  ('office_practice_areas', 1, 'المكتب بيشتغل في القضايا الجنائية والمخدرات، والمدني، والأسرة والأحوال الشخصية، والتجاري، والعقارات، وتأسيس الشركات، والنقض، والدستوري، والعمالي، والإداري، والعقود، والعرفي، وبيقبل قضايا من كل المحافظات.', 300, true, 'proposal TASK 06', 'owner approval 2026-10-26')
ON CONFLICT (key, version) DO NOTHING;

ALTER TABLE hazem.classifications ADD COLUMN info_topic text;

ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help',
  'reminder_failed',
  'office_info_missing'));

ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation', 'booking', 'appointment', 'payment', 'service', 'urf', 'price'));

GRANT EXECUTE ON FUNCTION hazem.price(text, int) TO office_api;
REVOKE ALL ON FUNCTION hazem.price(text, int) FROM PUBLIC;

-- migrate:down
-- 0015's two functions, as they were.
CREATE OR REPLACE FUNCTION hazem.price_service_order(p_order uuid, p_price int, p_exception boolean, p_due_on date, p_delivery text, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v hazem.service_orders;
  v_in_range boolean;
BEGIN
  SELECT * INTO v FROM hazem.service_orders WHERE id = p_order FOR UPDATE;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v.state NOT IN ('collecting', 'awaiting_price') THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  IF p_price IS NULL OR p_price <= 0 OR p_price > 1000000 OR p_due_on IS NULL OR p_due_on < current_date
     OR p_delivery NOT IN ('file', 'call') THEN
    RETURN jsonb_build_object('status', 'invalid');
  END IF;
  v_in_range := CASE v.kind
                  WHEN 'document_review' THEN p_price BETWEEN 1500 AND 3000
                  WHEN 'contract_drafting' THEN p_price >= 3000
                  ELSE p_price = 2000 END;
  IF NOT v_in_range AND NOT p_exception THEN
    RETURN jsonb_build_object('status', 'out_of_range');
  END IF;

  UPDATE hazem.service_orders
     SET state = 'awaiting_payment', price = p_price, price_exception = NOT v_in_range, due_on = p_due_on, delivery = p_delivery,
         priced_by = p_by, priced_at = now(), price_until = now() + interval '3 days', client_notice = 'pending', notice_kind = 'priced'
   WHERE id = v.id;
  INSERT INTO hazem.payments (service_order_id, amount_due) VALUES (v.id, p_price);
  RETURN jsonb_build_object('status', 'priced');
END $$;

CREATE OR REPLACE FUNCTION hazem.open_written_opinion(p_appointment uuid, p_due_on date, p_delivery text, p_by uuid)
RETURNS jsonb LANGUAGE plpgsql SET search_path = hazem, pg_temp AS $$
DECLARE
  v_a hazem.appointments;
  v_id uuid;
BEGIN
  SELECT * INTO v_a FROM hazem.appointments WHERE id = p_appointment;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('status', 'not_found');
  END IF;
  IF v_a.state <> 'completed' THEN
    RETURN jsonb_build_object('status', 'not_allowed');
  END IF;
  IF p_due_on IS NULL OR p_due_on < current_date OR p_delivery NOT IN ('file', 'call') THEN
    RETURN jsonb_build_object('status', 'invalid');
  END IF;
  IF EXISTS (SELECT 1 FROM hazem.service_orders WHERE opened_key = 'opinion:' || p_appointment)
     OR EXISTS (SELECT 1 FROM hazem.service_orders WHERE person_id = v_a.person_id AND kind = 'written_opinion' AND state NOT IN ('delivered', 'closed')) THEN
    RETURN jsonb_build_object('status', 'exists');
  END IF;
  INSERT INTO hazem.service_orders (person_id, intake_id, kind, state, appointment_id, opened_key, price, due_on, delivery,
                                    priced_by, priced_at, price_until, client_notice, notice_kind)
  VALUES (v_a.person_id, v_a.intake_id, 'written_opinion', 'awaiting_payment', v_a.id, 'opinion:' || p_appointment, 2000, p_due_on, p_delivery,
          p_by, now(), now() + interval '3 days', 'pending', 'priced')
  RETURNING id INTO v_id;
  INSERT INTO hazem.payments (service_order_id, amount_due) VALUES (v_id, 2000);
  RETURN jsonb_build_object('status', 'opened', 'id', v_id);
END $$;

DELETE FROM hazem.panel_audit WHERE target_type = 'price';
ALTER TABLE hazem.panel_audit DROP CONSTRAINT panel_audit_target_type_check;
ALTER TABLE hazem.panel_audit ADD CONSTRAINT panel_audit_target_type_check
  CHECK (target_type IN ('user', 'task', 'text', 'list', 'conversation', 'booking', 'appointment', 'payment', 'service', 'urf'));
DELETE FROM hazem.staff_tasks WHERE type = 'office_info_missing';
ALTER TABLE hazem.staff_tasks DROP CONSTRAINT staff_tasks_type_check;
ALTER TABLE hazem.staff_tasks ADD CONSTRAINT staff_tasks_type_check CHECK (type IN (
  'existing_client_message', 'late_reschedule', 'refund', 'price_quote', 'discount_request',
  'complaint', 'human_requested', 'safety_alert',
  'lawyer_direct_message', 'external_contact', 'unclear_message', 'urgent_request',
  'service_request', 'reschedule_or_cancel',
  'booking_unavailable', 'booking_cancelled', 'booking_message_failed', 'calendar_conflict', 'office_cancelled',
  'service_pricing', 'service_unpaid',
  'urf_review', 'urf_documents_missing', 'urf_help',
  'reminder_failed'));
ALTER TABLE hazem.classifications DROP COLUMN IF EXISTS info_topic;

-- The texts: TASK 06's off and gone (unless a reply used them); the priced versions give their place back.
UPDATE hazem.approved_texts SET active = false WHERE source = 'proposal TASK 06';
DELETE FROM hazem.approved_texts t WHERE t.source = 'proposal TASK 06'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
CREATE TEMP TABLE was_active ON COMMIT DROP AS
  SELECT key, version FROM hazem.approved_texts WHERE updated_by = 'migration 0018' AND active;
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'migration 0018';
UPDATE hazem.approved_texts p SET active = true FROM was_active w WHERE p.key = w.key AND p.version = w.version - 1;
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'migration 0018'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);

DROP FUNCTION IF EXISTS hazem.price(text, int);
DELETE FROM hazem.settings WHERE key LIKE 'price.%';
