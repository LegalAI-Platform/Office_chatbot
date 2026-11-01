-- migrate:up
-- DECISIONS v1.45 (TASK 18 §3.2): «حقايق المكتب», the office's facts the writer answers a client's question from (who the
-- consultation is with, how long, on which app, how booking and the refund go, the fees of a case, the written services,
-- what Hazem is). Two approved texts the panel shows and edits like the others; their prices are the panel's («الأسعار»),
-- never typed twice. They are never sent as a message: the writer reads them, and the code checks that every number and
-- word of a written reply is in them or in the approved text it replaces (ReplyGuard).
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('office_facts', 1, 'الاستشارة دايمًا مع المستشار سعد فتحي سعد نفسه.
استشارة التليفون أو الفيديو ربع ساعة بـ{{price_phone}} جنيه، والمستشار بيكلم العميل في ميعاده على واتساب على رقم العميل نفسه، أو FaceTime لو معاه آيفون.
المقابلة في المكتب نص ساعة بـ{{price_office}} جنيه.
الاستشارة العاجلة بالتليفون ربع ساعة بـ{{price_urgent}} جنيه، والمستشار بيكلم العميل أول ما يبقى فاضي بعد ما صورة التحويل توصل.
الحجز: العميل بيختار ميعاد من المواعيد الفاضية، والميعاد بيتحجزله لحد ما يبعت صورة التحويل، والمكتب بيراجع التحويل ويأكد الحجز.
لو العميل لغى خلال 24 ساعة من الدفع وقبل الميعاد، المبلغ بيرجع كامل.', 600, true, 'proposal TASK 18', 'migration 0045'),
  ('office_facts_more', 1, 'القضايا (خلع، طلاق، نفقة، ميراث…): المستشار بيسمع الموضوع في استشارة الأول ويحدد الأتعاب بعدها، وتمن الاستشارة بيتخصم منها لو اتفق مع المكتب خلال 30 يوم.
مراجعة العقد أو المستندات من {{price_review_min}} لـ{{price_review_max}} جنيه، وصياغة العقد من {{price_drafting_from}}، والإنذار الرسمي {{price_notice}}، والرأي المكتوب سعر الاستشارة + {{price_opinion}}، والسعر النهائي بعد ما المستشار يشوف الورق.', 600, true, 'proposal TASK 18', 'migration 0045'),
  ('office_facts_other', 1, 'الدفع مقدّم قبل الميعاد بالتحويل بس، على InstaPay أو محفظة كاش (مش في المكتب ولا بعد الاستشارة).
الجواز أو الطلاق العرفي {{price_urf_office}} جنيه بشهود المكتب، و{{price_urf_own}} بشهود العميل.
المكتب مابيعملش ورق حكومي (بطاقة، باسبور، سجل مدني).
كلام العميل وبياناته بتفضل عند المكتب بس.
حازم مساعد رقمي مش إنسان، والعميل يقدر يطلب حد من المكتب في أي وقت.', 600, true, 'proposal TASK 18', 'migration 0045');

-- migrate:down
DELETE FROM hazem.approved_texts WHERE key IN ('office_facts', 'office_facts_more', 'office_facts_other');
