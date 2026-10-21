# TASK 03 — الرسائل والميديا وتجميع الـ 8 ثواني

**الاعتماديات:** DECISIONS v1.3 (`docs/decisions/DECISIONS-v1.3.md`)، وTASK 02 (commit `8bad973`)
**البيئة:** Test/Staging فقط. **مفيش Meta حقيقية ولا OpenAI حقيقية في الاختبارات.**
**التاسك اللي بعده:** TASK 04، وأوله إعادة هيكلة الـ backend المتفق عليها، وبعدها فهم الرسالة بالـ AI.

> انسخ كل اللي جوه الـ code block وابعته للـ agent. الـ agent شغال جوه `K:\law-office-whatsapp`.
> صاحب المشروع لازم يراجع البرومت ده قبل التنفيذ.

---

```text
أنت Senior Integration Engineer متخصص في n8n وPostgreSQL، وبتعرف ASP.NET Core.

المشروع: نظام واتساب لمكتب محاماة («مكتب الأستاذ سعد فتحي سعد»)، والمساعد اسمه «حازم».
المعمارية (DECISIONS v1.3):
- n8n = طبقة الواتساب، والجزء الوحيد اللي بيكلّم Meta. ومفيش فيه أي قرار بيزنس (T-03).
- ASP.NET Core API في backend/ = العقل (T-07، T-17).
- الاتنين بيتكلموا بـ API داخلي موثّق في docs/hazem-internal-api.md.

مهمتك في TASK 03 إن كل اللي العميل بيبعته يتحول لـ «turn» واحد كامل وجاهز قبل ما يوصل لـ .NET:
1. الرسايل المتتابعة من نفس العميل تتجمع في turn واحد (8 ثواني).
2. الصور والمستندات والفويس والفيديو تتنزل من Meta على طول، وتتحفظ في S3 (Garage).
3. الفويس يتفرّغ لنص بـ OpenAI جوه n8n، والتسجيل الأصلي يتحفظ.
4. .NET يستلم نص ومراجع S3 بس، وعمره ما يستلم الملف نفسه.

==================================================
القسم 0: اقرا الأول (إجباري)
==================================================

اقرا الملفات دي بالكامل قبل أي تعديل:
- docs/decisions/DECISIONS-v1.3.md: المرجع الوحيد. أي تعارض معاه، وقف واسأل.
- docs/hazem-internal-api.md: العقد الحالي بين n8n و.NET.
- docs/sessions/2026-10-20-v1.3-session.md: اللي اتعمل في v1.3، وحالته.
- docs/whatsapp-integration.md: الـ workflows والإعدادات والـ credentials، والقسم الأخير
  فيه مذكرات template n8n رقم 17216 عن الميديا والفويس.
- database/migrations/0002_whatsapp.sql و0003_hazem_api.sql: الجداول، وwa_process_event،
  وwa_turns_claim، وwa_turns_finish.
- n8n/workflows/: META-WA-INBOUND، وMETA-WA-PROCESS-EVENT، وWA-FORWARD-TURN،
  وWA-HANDLE-TURN، وWA-SEND-ACTION، وWA-TEST-HARNESS.
- backend/src/Office.Core/Hazem/Turns.cs (الـ validation)، وResponders.cs (الـ echo).
- infra/tools/bin/wa-db-tests، وhazem-e2e، وhealthcheck.

قبل ما تبدأ شغّل الاختبارات دي. لازم كلها تبقى PASS، ولو فيه حاجة فاشلة وقف واكتبها:
  scripts/linux/healthcheck.sh، وwa-tests.sh، وbackend-test.sh، وhazem-e2e.sh

ممنوعات ثابتة طول التاسك:
- ممنوع أي تغيير في الـ stack: مفيش أدوات ولا خدمات ولا لغات جديدة. الخدمة الوحيدة المسموح
  تضيفها هي stub للاختبار بس (القسم 6)، وتكون في profile منفصل ومش بتشتغل افتراضيًا.
- ممنوع Meta حقيقية أو OpenAI حقيقية في أي اختبار.
- ممنوع أي صورة أو PDF أو مستند يروح لأي AI (T-09).
- ممنوع n8n ياخد أي قرار بيزنس أو يكتب أي رد للعميل (T-03).
- ممنوع إعادة هيكلة الـ backend. ده متفق عليه لأول TASK 04 (القسم 5).
- ممنوع EF migrations. dbmate بس (T-21).
- ممنوع commit من غير ما صاحب المشروع يطلب.

==================================================
القسم 1: تجميع الـ 8 ثواني (migration 0004)
==================================================

السلوك المطلوب (من ملف الواتساب الأصلي): «لو العميل بعت كذا رسالة ورا بعض، حازم يستنى
8 ثواني ويرد مرة واحدة على الكل».

1.1 القاعدة
- turn = كل رسايل عميل واحد (wa_id) اللي حالتها pending وجاهزة، ومش من جروب.
- الـ turn يتقفل لما يعدّي 8 ثواني من غير رسالة جديدة من العميل ده (quiet period)، يعني
  آخر رسالة pending ليه عمرها 8 ثواني أو أكتر.
- حد أقصى: لو العميل فضل يبعت من غير توقف، الـ turn يتقفل بعد 60 ثانية من أول رسالة فيه،
  عشان العميل ما يستناش للأبد. اتنين الأرقام دول (8 و60) يبقوا إعدادات في app.settings
  (whatsapp.group_quiet_seconds = 8، وwhatsapp.group_max_seconds = 60)، مش مكتوبين في الكود.
- **الساعة اللي بنقيس بيها:** الـ 8 ثواني والـ 60 ثانية بيتحسبوا من received_at (وقت السيرفر
  عندنا)، **مش** من sent_at (وقت Meta)، لأن sent_at ممكن يكون متأخر أو متلخبط الترتيب. sent_at
  بيتستخدم بس في ترتيب الرسايل جوه الـ turn. والاختبارات في SQL بترجّع received_at لورا
  (backdate) بدل ما تستنى بـ sleep.
- رسالة «جاهزة» = نص، أو ميديا حالتها النهائية اتحددت (stored أو failed أو too_large أو
  rejected). الميديا اللي لسه بتتنزل بتأخّر الـ turn كله، لحد 60 ثانية بالحد الأقصى. بعدها
  الرسالة تدخل الـ turn بـ media.status = 'failed'، و**في نفس الـ transaction** صف wa_media
  يتحدث لـ failed بسبب timeout. ولو التنزيل خلص متأخر بعد كده، ممنوع يقلب الحالة لـ stored
  (wa_media_stored بيرفض أي صف حالته مش downloading). كده الداتابيز دايمًا بتقول نفس اللي
  .NET اتقاله.
- **ترتيب الردود:** لو العميل عنده turn مفتوح، ما يتعملوش turn تاني. «مفتوح» يعني حالته
  processing **أو** pending (turn اترجّع بعد ما الـ API فشل). الـ turn الـ pending بيتحاول
  تاني بنفس الـ turn_id ونفس الرسايل، والرسايل الجديدة بتستنى الـ turn اللي بعده. من غير
  القاعدة دي، رسايل جديدة ممكن تعمل turn تاني ويترد عليها قبل الأول.
- رسايل الجروبات تفضل ignored_group زي ما هي. والأنواع اللي ملهاش محتوى (reaction مثلًا)
  بتدخل الـ turn عادي، و.NET هو اللي يقرر يعمل بيها إيه.

1.2 قاعدة البيانات (database/migrations/0004_turn_grouping_media.sql)
- جدول app.wa_turns:
    turn_id text PK، وwa_id، وstatus (processing / processed / failed / pending)،
    وclaimed_at، وattempts، وlast_error jsonb، وcreated_at، وupdated_at.
  turn_id الجديد = 'wt:' + uuid. بيتعمل مرة واحدة ويتحفظ.
- عمود جديد wa_inbound_messages.turn_id، وهو FK على app.wa_turns.
- wa_turns_claim يتعمله replace بنفس الاسم ونفس شكل الـ output (array من turns)، والمنطق
  الجديد كالآتي:
  a) الأول: أي turn مفتوح جاهز يتحاول تاني، يعني pending، أو processing وفات عليه
     whatsapp.turn_stale_seconds. بيتاخد بنفس الـ turn_id ونفس الرسايل بالظبط، من غير ما رسايل
     جديدة تدخل عليه. كده .NET يرجّع duplicate بنفس الإجراءات، ومفيش حاجة تتبعت مرتين (T-19).
  b) بعدها: عملاء (wa_id) عندهم رسايل pending جاهزة، والـ quiet period أو الحد الأقصى خلص،
     وما عندهمش turn مفتوح (1.1). لكل عميل اعمل turn واحد واربط بيه كل رسايله، والحالة
     تبقى processing.
  c) القفل لكل عميل: pg_try_advisory_xact_lock(hashtext('wa-turn:' || wa_id)). لو العميل
     مقفول، عدّيه (skip)، عشان claim اتنين في نفس الوقت ما يستنوش بعض أبدًا. ولو استخدمت
     FOR UPDATE SKIP LOCKED كمان، اتأكد بنفسك ومن الاختبار إن الاتنين مع بعض ما بيعملوش deadlock.
  d) whatsapp.turn_stale_seconds إعداد جديد قيمته 300. ده بدل الـ 5 دقايق اللي مكتوبة ثابتة
     في 0003، وبيطبّق على الـ turns وعلى تنزيل الميديا الواقف. hazem-e2e بيحطه قيمة قصيرة
     لاختبار restart الـ n8n، وبيرجّعه 300 في الآخر، والـ healthcheck بيفحص إنه 300.
  e) خلّي الـ parameter p_only_wa_ids زي ما هو في wa_turns_claim، لأن wa-db-tests وhazem-e2e
     بيعتمدوا عليه عشان ما يتسابقوش مع الـ schedule بتاع كل دقيقة. ونفس الحاجة في wa_media_claim.
  f) الـ output: turn_id، وwa_id، وcontact_name، وmessages مترتبة بـ sent_at بالشكل الجديد
     (القسم 4).
- wa_turns_finish(p_turn_id text, p_status text, p_error jsonb):
  - بيحدّث الـ turn ورسايله في transaction واحدة.
  - 'pending' معناها «رجّع الـ turn يتحاول تاني». الـ turn_id بيفضل محفوظ والرسايل مربوطة
    بيه، عشان المحاولة الجاية تبعت نفس الـ turn_id.
  - النسخة القديمة اللي بتاخد wamid[]: شيلها، وحدّث كل الـ workflows اللي بتناديها.
    اكتب في التقرير إن ده اتشال.
- إعدادات جديدة في app.settings: group_quiet_seconds، وgroup_max_seconds،
  وturn_stale_seconds، والإعدادات اللي في القسم 2 و3 و6.
- **الصفوف اللي شغالة وقت تطبيق 0004:** أي رسالة نصية حالتها processing بالنظام القديم
  (turn_id = 'wa:<wamid>') ترجع pending من غير turn_id، عشان تتجمع من جديد بالقاعدة الجديدة.
  ولو ده ممكن يعمل رد مكرر، الـ idempotency keys هي اللي بتمنعه، فوضّح ده في التقرير. صفوف
  hazem.turns القديمة تفضل زي ما هي، ومحدش يلمسها. ورسايل الميديا اللي pending من قبل 0004
  يتعملها صف في wa_media بحالة failed (السبب: received_before_task03)، لأن روابط Meta بتاعتها
  انتهت خلاص، وبعدين تدخل turns عادي.
- migrate up ثم down ثم up لازم يشتغلوا. الـ down بيرجّع نسخة 0003 من الـ functions.

1.3 مين بيشغّل التجميع (n8n)
- META-WA-INBOUND: بعد PROCESS-EVENT وتنزيل الميديا (القسم 2)، Wait قد group_quiet_seconds
  + ثانية واحدة، وبعدين WA-FORWARD-TURN. خد قيمة الـ Wait من الإعدادات لو n8n بيسمح، ولو
  لأ اكتبها ثابتة ووثّق ده.
- الـ schedule بتاع كل دقيقة في WA-FORWARD-TURN يفضل شغال كضمان، عشان لو حاجة اتأخرت أو
  n8n عمل restart.
- لو الاتنين اشتغلوا في نفس الوقت، القفل اللي في 1.2 b هو اللي بيمنع التكرار.
- القرار: **قفل في PostgreSQL، ومش Redis.** السبب إن الرسايل والـ turns في PostgreSQL، والقفل
  في نفس الـ transaction بيمنع أي حالة فيها ناحية اتحدثت والتانية لأ. Redis يفضل للحاجات
  اللي جاية (rate limiting). اكتب السبب ده في docs/whatsapp-integration.md.

1.4 لازم يتضمن
- مفيش رسالة تتعالج مرتين: ولا في turn اتنين، ولا من عميلين في نفس الوقت.
- مفيش رسالة تضيع: لو .NET واقع، أو n8n عمل restart في النص، أو الـ turn فضل processing.
- الترتيب: turn العميل التاني ما يبدأش غير لما الأول يخلص.

==================================================
القسم 2: الميديا
==================================================

2.1 التنزيل: workflow جديد WA-MEDIA-FETCH (sub-workflow)
- بيتنادى من META-WA-INBOUND بعد PROCESS-EVENT، للرسايل الجديدة اللي نوعها image أو document
  أو audio أو video أو sticker. والـ schedule بتاع كل دقيقة بيعيد أي ميديا لسه pending.
- الخطوتين (من template 17216):
  1. GET {graph_base_url}/{graph_api_version}/{media_id} بالـ credential meta-whatsapp-token،
     وبيرجّع url، وmime_type، وsha256، وfile_size.
  2. تنزيل الـ url بنفس الـ token، **على طول**، لأن الرابط بينتهي بعد دقايق.
- الحدود:
  - حجم الملف من whatsapp.media_max_mb (50 افتراضيًا، من ملف الواتساب الأصلي). الملف الأكبر
    من كده ما يتنزلش، وحالته تبقى too_large ومعاها الحجم. .NET هو اللي يقرر يقول للعميل إيه.
  - mime_type لازم يكون من قائمة مسموحة (صور، وPDF، ومستندات Office، وصوت، وفيديو).
    الملفات التنفيذية والسكريبتات والمضغوطة حالتها rejected، وما تتحفظش.
  - لو Meta بعتت sha256، اتأكد منه بعد التنزيل. لو مش مطابق، الحالة failed.
  - 3 محاولات بـ backoff. بعدها الحالة failed والسبب يتسجل، والـ turn يكمّل.
- مفيش أي AI في الـ workflow ده.

2.2 التخزين
- الـ bucket يتحدد بقاعدة أمان ثابتة، مش بقرار بيزنس:
  - image وdocument → office-sensitive دايمًا، لأن ممكن تكون بطاقة أو ورق عرفي أو إيصال
    أو عقد، وn8n ما يقدرش يعرف المحتوى (وممنوع يعرفه).
  - audio وvideo وsticker → office-media.
- الـ key: wa/<yyyy>/<mm>/<wamid>.<ext>. ممنوع رقم العميل أو اسمه أو اسم الملف الأصلي في الـ key.
- n8n S3 credential: اسمه office-s3، والمفاتيح S3_N8N_* من .env، والـ endpoint
  http://garage:3900، والـ region من S3_REGION، وforce path style.
  n8n-import يعمله من .env زي باقي الـ credentials المحلية.
- مفتاح n8n في Garage بيقرا ويكتب في office-media وoffice-sensitive بس، وده موجود من TASK 01.
- metadata في الـ object: wamid وmime_type بس.

2.3 قاعدة البيانات (في نفس migration 0004)
- جدول app.wa_media:
    id، وinbound_message_id FK، وmeta_media_id، وkind (image/document/audio/video/sticker)،
    وmime_type، وfile_size، وsha256، وfilename (اسم المستند من Meta، يتحفظ هنا بس)،
    وbucket، وobject_key، وstatus (pending/downloading/stored/failed/too_large/rejected)،
    وattempts، وlast_error، وstored_at.
    وللفويس: transcript text، وtranscript_status (not_needed/pending/done/failed)،
    وtranscript_model، وtranscribed_at.
    UNIQUE(inbound_message_id).
- wa_process_event يعمل صف في wa_media لكل رسالة ميديا جديدة بحالة pending، في نفس الـ
  transaction. اكتبه بـ CREATE OR REPLACE في 0004، وحافظ على كل سلوكه القديم.
- functions صغيرة: wa_media_claim للتنزيل (SKIP LOCKED)، وwa_media_stored، وwa_media_failed،
  وwa_media_transcribed. نفس أسلوب 0002/0003: كل القواعد في SQL، وn8n بينادي بس.

==================================================
القسم 3: الفويس (T-10)
==================================================

- بعد ما الـ audio يتحفظ في S3: workflow جديد WA-TRANSCRIBE (sub-workflow)، أو خطوة جوه
  WA-MEDIA-FETCH. اختار الأبسط ووثّقه.
- OpenAI audio transcription عن طريق HTTP Request:
  - الـ endpoint: {openai_base_url}/audio/transcriptions.
  - الـ credential: openai-transcription (Header Auth: Authorization: Bearer <key>).
    صاحب المشروع هو اللي يعمله في n8n بنفسه (T-08). ممنوع المفتاح يبقى في .env أو في ملف أو
    في الـ logs أو في الشات.
  - الموديل من ai.transcription_model في app.settings. ما تخمنش اسم موديل نهائي: حط
    قيمة افتراضية موثّقة، واكتب إن صاحب المشروع يثبّتها بعد ما يجرّب على عينات مصرية (T-08).
  - language = ar.
  - تظبيط نوع الملف قبل الإرسال: الفويس بتاع واتساب بييجي audio/ogg; codecs=opus، فيتبعت
    كـ audio/ogg باسم .ogg. وأي mapping تاني لازم يتوثق.
- التسجيل الأصلي يفضل في S3، والـ transcript يتحفظ في wa_media.
- لو التفريغ فشل: transcript_status = failed، والـ turn يكمّل والـ transcript = null.
  .NET هو اللي يقرر يقول إيه.
- الفيديو ما يتفرّغش في التاسك ده. اكتب ده في التقرير.
- ممنوع أي ملف غير audio يروح لـ OpenAI. لازم يبقى فيه check صريح قبل الـ HTTP node، واختبار
  ليه (القسم 6).

==================================================
القسم 4: العقد بين n8n و.NET (docs/hazem-internal-api.md)
==================================================

حدّث الملف بشكل الرسالة الجديد جوه الـ turn. **لازم يفضل متوافق مع الرسايل النصية:** أي
رسالة نصية لازم توصل بنفس الشكل القديم بالظبط.

{
  "turn_id": "wt:0b6c…",                 ← نص معتم (opaque)، 1–200 حرف. .NET ما يحللهوش
  "wa_id": "201001234567",
  "contact_name": "…",
  "messages": [
    { "wamid": "…", "type": "text", "text": "…", "sent_at": "…", "context_wamid": null },
    { "wamid": "…", "type": "image", "caption": "…", "sent_at": "…", "context_wamid": null,
      "media": { "status": "stored", "bucket": "office-sensitive", "key": "wa/2026/10/….jpg",
                 "mime_type": "image/jpeg", "size_bytes": 123456, "sha256": "…" } },
    { "wamid": "…", "type": "audio", "sent_at": "…",
      "media": { "status": "stored", "bucket": "office-media", "key": "…", "mime_type": "audio/ogg",
                 "size_bytes": 23456, "sha256": "…", "voice": true },
      "transcript": { "status": "done", "text": "…", "language": "ar" } },
    { "wamid": "…", "type": "document", "caption": null, "sent_at": "…",
      "media": { "status": "too_large", "mime_type": "application/pdf", "size_bytes": 73400320,
                 "filename": "…" } },
    { "wamid": "…", "type": "location", "location": { "latitude": 30.0, "longitude": 31.2, "name": null } },
    { "wamid": "…", "type": "reaction", "reaction": { "emoji": "👍", "message_wamid": "…" } },
    { "wamid": "…", "type": "interactive", "interactive": { "kind": "button_reply", "id": "…", "title": "…" } },
    { "wamid": "…", "type": "button", "button": { "payload": "…", "text": "…" } }
  ]
}

الأنواع المسموحة: text، وimage، وdocument، وaudio، وvideo، وsticker، وlocation، وreaction،
وinteractive (button_reply وlist_reply)، وbutton (أزرار الـ templates السريعة). الأنواع
interactive وbutton لازم يتضافوا من دلوقتي، عشان أزرار الـ templates اللي جاية في تاسكات بعدين
ما توصلش unsupported.

القواعد اللي لازم تتكتب في العقد:
- القائمة المسموحة لـ type، وأي نوع تاني يوصل type = "unsupported".
- media بيبقى موجود بس لأنواع الميديا. bucket وkey موجودين بس لو status = stored.
- **مفيش URL ولا محتوى ملف أبدًا** (T-09). .NET بياخد مراجع بس، وما يقدرش ينزّل الملف من S3
  في التاسك ده، لأن ملوش مفتاح.
- اسم الملف الأصلي ممكن يكون فيه بيانات شخصية. بيتبعت في media.filename، و.NET ممنوع
  يكتبه في الـ logs.
- turn_id بقى 'wt:<uuid>'. الـ turns القديمة 'wa:<wamid>' اللي في hazem.turns تفضل زي ما هي.
- حدّث الـ sequence diagram، وقسم «Message states»، وقسم «Testing without Meta».

==================================================
القسم 5: تعديلات .NET (أقل حاجة ممكنة)
==================================================

المسموح بس:
- TurnMessage ياخد الحقول الجديدة: caption، وmedia، وtranscript، وlocation، وreaction،
  وinteractive، وbutton.
- Validation:
  - type من القائمة.
  - media مطلوب لأنواع الميديا، وstatus من القائمة.
  - bucket من {office-media, office-sensitive}.
  - key مطابق للشكل wa/yyyy/mm/....
  - مفيش أي حقل فيه "://"، يعني مفيش روابط.
  - caption وtranscript.text لحد 4096 حرف.
  - عدد الرسايل لحد 50 زي ما هو.
  - الغلط بيرجّع 400 بـ problem details زي النهارده.
- الـ echo (test only): لو فيه transcript، يرد بـ «تم استلام رسالتك الصوتية (اختبار): <أول
  60 حرف>». ولو ميديا، يرد بـ «تم استلام <النوع> (اختبار) ✅». ده عشان hazem-e2e يتأكد إن الشكل
  وصل. ومن غير الـ echo، الميديا ما ليهاش أي رد في التاسك ده.
- الـ logs: ممنوع نص الرسايل، والـ transcripts، وأسماء الملفات. الـ turn_id والأنواع والأعداد بس.
- هيكل الـ backend يفضل زي ما هو:
  - ممنوع تنقل الـ DTOs برا Office.Core.
  - ممنوع ITurnResponder.
  - ممنوع entities أو state machines.
  ده كله متفق عليه مع صاحب المشروع لأول TASK 04.

==================================================
القسم 6: الاختبار من غير Meta ومن غير OpenAI
==================================================

6.1 الـ Stubs
- إعدادات في app.settings:
  - whatsapp.graph_base_url (افتراضي https://graph.facebook.com)
  - ai.openai_base_url (افتراضي https://api.openai.com/v1)
- service جديد meta-stub، في profile اسمه tests بس، ومش بيشتغل افتراضيًا. بيعمل الآتي:
  - GET /{version}/{media_id}: بيرجّع JSON زي Meta، والـ url فيه بيشاور على الـ stub نفسه.
  - تنزيل ملفات اختبار ثابتة (صورة صغيرة، وPDF صغير، وogg صغير) بالـ sha256 الصح.
  - media_id خاص بيرجّع ملف sha256 بتاعه غلط، وواحد بيرجّع حجم أكبر من الحد.
  - POST /v1/audio/transcriptions: بيرجّع transcript ثابت.
  - **بيسجّل كل request وصله**، عشان الاختبار يتأكد إن OpenAI ما استلمش غير ملفات audio.
  اعمله **minimal API بـ .NET في backend/، في project اسمه Office.TestStub**، والـ service
  بتاعه في profile tests بس. ما تستخدمش office-tools، لأنه alpine/bash ومفيهوش HTTP server،
  وأي server مكتوب بـ bash هيبقى هش. وOffice.TestStub ما يتحطش في الـ image بتاع api.
- hazem-e2e بيحوّل الإعدادين للـ stub، وwhatsapp.dry_run = true، وبيرجّعهم في الآخر
  حتى لو الاختبار فشل (trap).
- الـ healthcheck بيفشل لو أي إعداد من الاتنين مش على قيمته الافتراضية، أو dry_run = true.
- **الـ credentials في الاختبار** (أهم نقطة في القسم ده):
  الوضع النهارده: صاحب المشروع لسه ما عملش أي credential لـ Meta ولا لـ OpenAI. وWA-MEDIA-FETCH
  محتاج meta-whatsapp-token، والتفريغ محتاج openai-transcription. فيه فخّين:
  (a) n8n-import بيبدّل CRED:<name> بالـ ID **وقت الاستيراد**. يعني أي credential يتعمل بعد
      الاستيراد ما بيتستخدمش لحد الاستيراد اللي بعده.
  (b) CLI بتاع n8n 2.43 فيه import:credentials، **بس مفيهوش أمر يمسح credential**.
  المطلوب:
  - الاختبارات تشتغل كاملة من غير أي credential حقيقي لـ Meta أو OpenAI.
  - **ممنوع تقرا أو تكتب فوق أي credential حقيقي موجود.** لو credential بالاسم ده موجود،
    سيبه زي ما هو. والاختبار بيروح للـ stub برضه، لأن graph_base_url وopenai_base_url متحولين.
  - **الـ stub ممنوع يسجّل قيمة Authorization أو أي header سري.** بيسجّل إن الـ header موجود
    بس. ده لأن لو credential حقيقي موجود، قيمته هتوصل للـ stub جوه شبكة Docker.
  - **بعد الاختبار n8n يرجع بالظبط زي ما كان قبله**، حتى لو الاختبار فشل (trap). يعني ما يفضلش
    أي credential مزيف يخبّي التحذير «credentials not found».
  الطريقة المقترحة (صمّمها واتأكد منها بنفسك في n8n 2.43، ووثّقها في docs/runbooks/whatsapp.md):
  1. قبل n8n-import: لكل credential من الاتنين **لو مش موجود بالاسم بس**، اعمل credential
     مؤقت بـ ID ثابت (مثلًا TestMetaToken001 وTestOpenAiKey001) وقيمة 'stub-not-a-real-key'،
     بـ import:credentials. وسجّل في ملف مؤقت أنهي واحد اتعمل.
  2. n8n-import، عشان الـ workflows تاخد الـ IDs.
  3. الاختبار.
  4. في الآخر: امسح **الـ IDs الثابتة اللي اتعملت في الخطوة 1 بس**. اتأكد بنفسك من الجداول
     اللي لازم تتمسح في قاعدة n8n (مثلًا credentials_entity وshared_credentials)، والشرط يبقى
     على الـ ID **و**الاسم مع بعض. بعدها n8n-import تاني، عشان الـ workflows ترجع للـ
     placeholders.
  5. اتأكد إن تحذير «credentials not found» رجع زي ما كان قبل الاختبار، وده يبقى PASS/FAIL
     في hazem-e2e.
  لو لقيت طريقة أبسط وأأمن في n8n 2.43، استخدمها ووثّق السبب.

6.2 الاختبارات المطلوبة (لازم كلها PASS)

wa-db-tests (rules في SQL، بالـ fixtures):
[ ] 3 رسايل من نفس العميل في أقل من 8 ثواني ← turn واحد فيه الـ 3 بالترتيب.
[ ] رسالة، وبعدها 9 ثواني، وبعدها رسالة ← 2 turns.
[ ] claim قبل ما الـ 8 ثواني تخلص ← مفيش turn.
[ ] عميل بيبعت كل 5 ثواني ← الـ turn يتقفل عند الـ 60 ثانية.
[ ] عميلين في نفس الوقت ← 2 turns منفصلين.
[ ] claim مرتين في نفس الوقت (جلستين psql) ← كل رسالة في turn واحد بس.
[ ] turn شغال (processing) ← رسايل العميل الجديدة ما تتاخدش لحد ما يخلص.
[ ] turn اترجّع pending (الـ API فشل)، وبعدها رسايل جديدة ← الـ claim الجاي بيرجّع الـ turn
    القديم بنفس turn_id ونفس الرسايل بس، والجديدة بتستنى الـ turn اللي بعده.
[ ] turn فضل processing أكتر من turn_stale_seconds ← يتاخد تاني بنفس turn_id ونفس الرسايل،
    من غير الجديدة.
[ ] finish pending ثم claim ← نفس turn_id.
[ ] claim في نفس الوقت على عميل مقفول ← بيعدّيه فورًا من غير ما يستنى (pg_try_advisory_xact_lock).
[ ] ميديا pending ← الـ turn يستنى. ولما تبقى stored ← الـ turn يتعمل. وبعد 60 ثانية من غير
    تنزيل ← الـ turn يتعمل بـ media.status = failed، وصف wa_media بقى failed (timeout) في نفس
    الـ transaction.
[ ] تنزيل خلص بعد الـ timeout ← wa_media_stored بيرفض، والحالة تفضل failed.
[ ] الحسابات بـ received_at: رسالة sent_at بتاعتها قديمة بس received_at جديد ← لسه جوه الـ quiet period.
[ ] تطبيق 0004 وفيه رسالة نصية processing بالنظام القديم ← رجعت pending واتجمعت من جديد.
[ ] رسالة جروب ← ما تدخلش أي turn.
[ ] رسالة نصية لوحدها ← نفس الشكل القديم بالظبط (عدا turn_id).
[ ] office_api ما يقدرش يقرا app.wa_media، وoffice_app ما يقدرش يقرا hazem.*.

WA-TEST-HARNESS (n8n من غير Meta): حدّث الحالات الموجودة وضيف عليها:
[ ] حدث فيه image ← صف في wa_media بحالة pending.
[ ] نفس الحدث مرتين ← صف واحد بس.

hazem-e2e (الدورة كاملة: meta-stub، وdry_run، وecho):
[ ] نصين وصورة من نفس الرقم في أقل من 8 ثواني ← turn واحد، ورد واحد بس من .NET.
[ ] الصورة اتحفظت في office-sensitive بالـ key الصح، والـ sha256 مطابق.
[ ] PDF ← office-sensitive. فويس ← office-media، والـ transcript وصل لـ .NET، والرد فيه نص الـ stub.
[ ] **OpenAI stub ما استلمش غير ملف الـ ogg** (T-09). أي request فيه image أو pdf يبقى FAIL.
[ ] ملف أكبر من الحد ← too_large، ومفيش تنزيل، والـ turn وصل لـ .NET.
[ ] sha256 غلط ← failed، والـ turn وصل لـ .NET.
[ ] mime ممنوع (مثلًا application/x-msdownload) ← rejected، ومفيش حاجة اتحفظت.
[ ] .NET واقع وقت الـ turn ← الـ turn يرجع pending، ولما .NET يرجع يتبعت بنفس turn_id، ورد واحد بس.
[ ] restart لـ n8n وفيه turn شغال ← بعد turn_stale_seconds (قيمة قصيرة في الاختبار بس) يتعاد
    بنفس turn_id ومن غير تكرار.
[ ] الـ JSON اللي .NET استلمه مفيهوش أي "://".
[ ] بعد الاختبار: مفيش credential مؤقت فاضل، والـ credentials الحقيقية (لو موجودة) ما اتغيرتش،
    وتحذير «credentials not found» زي ما كان قبل الاختبار.
[ ] سجل الـ stub مفيهوش أي قيمة Authorization.
[ ] turn_stale_seconds رجع 300، وgraph_base_url وopenai_base_url رجعوا للقيم الافتراضية.
[ ] الحالات القديمة (18) لسه PASS.

backend-test:
[ ] validation للشكل الجديد: كل type، وmedia ناقص أو غلط، وbucket برا القائمة، وkey بشكل
    غلط، وأي حقل فيه "://"، وحدود الطول. الغلط بيرجّع 400.
[ ] رسالة نصية بالشكل القديم ← لسه مقبولة.
[ ] الـ echo للفويس والميديا.
[ ] الـ logs ما فيهاش caption ولا transcript ولا filename. اختبرها بـ logger في الذاكرة.

healthcheck (ضيف):
[ ] migration 0004 متطبق، وجداول wa_turns وwa_media موجودة، والصلاحيات صح.
[ ] graph_base_url وopenai_base_url على القيم الافتراضية، وturn_stale_seconds = 300، وdry_run = false.
[ ] مفيش credential من الـ IDs الثابتة بتاعة الاختبار (TestMetaToken001 وTestOpenAiKey001) فاضل في n8n.
[ ] credential office-s3 موجود. وopenai-transcription: موجود، أو تحذير واضح لو مش موجود
    (مش FAIL، لأن صاحب المشروع لسه هيعمله).
[ ] مفيش turn بقاله processing أكتر من 10 دقايق.
[ ] مفتاح n8n يقدر يكتب في office-media وoffice-sensitive (موجود من TASK 01، اتأكد إنه لسه PASS).

==================================================
القسم 7: التوثيق
==================================================

- docs/hazem-internal-api.md: القسم 4.
- docs/whatsapp-integration.md:
  - الـ workflows الجديدة والمتعدلة.
  - قواعد التجميع وسبب اختيار القفل في PostgreSQL.
  - قواعد الـ bucket.
  - الإعدادات الجديدة.
  - credential office-s3 وopenai-transcription.
  - الـ stub.
- docs/runbooks/whatsapp.md:
  - turn واقف processing.
  - ميديا فشلت تتنزل.
  - التفريغ فشل.
  - مفتاح OpenAI اتلغى.
  - إزاي تنقل الـ stub للقيم الحقيقية والعكس.
- docs/meta-setup.md: خطوة لعمل credential openai-transcription (🔒 صاحب المشروع بس).
- ملف docs/sessions/<التاريخ>-task03.md بنفس شكل ملف v1.3.

==================================================
القسم 8: شروط القبول
==================================================

[ ] كل الاختبارات اللي فاتت (healthcheck، وwa-tests، وbackend-test، وhazem-e2e) لسه PASS،
    ومعاها الإضافات اللي في القسم 6.2.
[ ] الاختبارات بتشتغل بسكريبتات Linux وWindows الاتنين.
[ ] migrate up ثم down ثم up على 0004.
[ ] n8n-import من غير أي تدخل يدوي (ماعدا credentials بتوع صاحب المشروع).
[ ] مفيش secret في git.
[ ] في آخر التاسك: dry_run = false، وgraph_base_url وopenai_base_url على القيم الافتراضية،
    وturn_stale_seconds = 300، والـ stub واقف، ومفيش أي credential اختبار فاضل في n8n.
[ ] التوثيق في القسم 7 كامل.
[ ] قائمة «محتاج Meta حقيقية» في التقرير.

==================================================
القسم 9: التقرير النهائي
==================================================

1. الوضع قبل ما تبدأ (الاختبارات).
2. الملفات اللي اتعملت أو اتعدلت.
3. شرح التجميع وضماناته (مفيش تكرار، ومفيش ضياع، والترتيب)، ودليل من الاختبارات.
4. الميديا والفويس: الـ flow، والـ buckets، والحدود.
5. التغييرات في العقد وفي .NET.
6. نتيجة كل اختبار.
7. أي اختلاف عن البرومت ده أو عن DECISIONS v1.3، والسبب.
8. **محتاج Meta حقيقية:**
   - تنزيل ميديا حقيقية، وإن رابطها بينتهي فعلًا.
   - أنواع mime الحقيقية.
   - فويس حقيقي بالعامية المصرية، وجودة التفريغ واختيار الموديل.
   - مستندات بأسماء عربي.
   - حدود الحجم الحقيقية عند Meta.
9. اللي TASK 04 محتاجه: شكل الـ turn الجديد، وإن .NET ملوش وصول للملفات، والقرارات اللي
   اتأجلت (الرد على too_large أو failed أو unsupported).
```

---

## ملاحظات ليك (مش للـ agent)

- **فيه credential جديد إنت هتعمله بنفسك:** `openai-transcription` في n8n، ومعاه مفتاح OpenAI للفويس. الاختبارات مش محتاجاه، لأنها بتستخدم stub.
- **كل الصور والمستندات بتروح الـ bucket الحساس** حتى لو صورة عادية. n8n مش بيعرف (وممنوع يعرف) هي بطاقة ولا لأ، فاخترنا الأأمن.
- **ردود زي «الملف كبير» أو «النوع ده مش مدعوم» مش في التاسك ده.** n8n بيبعت الحالة لـ .NET بس، والرد هيتكتب في TASK 04 من النصوص المعتمدة.
- **الملف الأكبر من 50 ميجا:** ملف الواتساب الأصلي بيقول نبعت للعميل «لينك رفع مؤقت». ده محتاج صفحة رفع، فهيتعمل في تاسك بعدين. هنا الحالة بتتسجل بس.
