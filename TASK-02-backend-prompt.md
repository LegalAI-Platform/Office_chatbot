# TASK 02 — هيكل الـ Backend (ASP.NET Core) + ربط Meta WhatsApp Cloud API

**الاعتماديات:** `docs/decisions/DECISIONS-v1.1.md` + TASK 01 (commit `e7b82e2`)
**بيحل محل:** `TASK-02-prompt.md` (نسخة n8n)
**البيئة:** Test/Staging فقط
**التاسك اللي بعده:** TASK 03 — الرسائل والميديا وتجميع الـ 8 ثواني

> انسخ كل اللي جوه الـ code block وابعته للـ agent. الـ agent شغال جوه الـ repository نفسه.

---

```text
أنت Senior .NET Backend Engineer ومعاك خبرة في Meta WhatsApp Cloud API وPostgreSQL وDocker.

مهمتك بناء الهيكل الأساسي لـ backend بـ ASP.NET Core لنظام واتساب لمكتب محاماة
(«مكتب الأستاذ سعد فتحي سعد»، والمساعد الآلي اسمه «حازم»)، وربطه بـ Meta WhatsApp
Cloud API: استقبال الأحداث والتحقق منها وحفظها، والإرسال، وتتبع الحالة، والقوالب.
مفيش أي منطق خاص بحازم في التاسك ده (ردود، أو تصنيف، أو أسعار).

المراجع، اقراها كلها قبل ما تبدأ:
- docs/decisions/DECISIONS-v1.1.md: المرجع الوحيد للقرارات. اقرا بالذات الجزء ٢
  (T-03 وT-17 لحد T-23) والجزء ٣ (M-02 وM-03) والجزء ٤ (S-04).
  لو أي حاجة هنا اتعارضت معاه، هو اللي يكسب، واذكر التعارض.
- README.md وdocs/infrastructure.md وdocs/env-variables.md: البنية من TASK 01.
- docker-compose.yml وinfra/ وscripts/.

==================================================
القسم 0: تنظيف الـ repository قبل ما تبدأ (إلزامي)
==================================================

جلسة سابقة بدأت TASK 02 بـ n8n قبل ما الاستاك يتغير في v1.1، وسابت شغل كتير
من غير commit. ممنوع تمسحه، وممنوع تبني عليه وهو في master.

1. شغّل git status وسجّل القائمة في التقرير.
2. اعمل branch اسمه archive/task02-n8n من الـ HEAD الحالي، وعليه commit فيه كل شغل
   نسخة n8n اللي ما اتعملهوش commit، وبالتحديد:
   docker-compose.yml، وscripts/linux/_common.sh، وscripts/windows/_common.ps1،
   وTASK-02-prompt.md، وdatabase/migrations/0002_whatsapp.sql، وinfra/tools/bin/wa-db-tests،
   وn8n/scripts/، وn8n/templates/، وn8n/workflows/*.json (الجداد)، وn8n/fixtures/،
   وscripts/*/n8n-export*، وn8n-import*، وtunnel-refresh*، وtunnel-down*، وwa-tests*،
   وdocs/meta-setup.md.
   رسالة الـ commit: «Archive: TASK 02 n8n attempt (superseded by DECISIONS v1.1)».
   ممنوع يدخل الـ commit ده: .env، أو exports/، أو docs/decisions/DECISIONS-v1.1.md،
   أو TASK-02-backend-prompt.md.
3. ارجع لـ master. الملفات المتعدّلة ترجع لنسخة commit e7b82e2. والملفات اللي هتحتاجها
   من الأرشيف هاتها بـ git checkout archive/task02-n8n -- <path>:
   - n8n/fixtures/whatsapp/*.json ← انقلها لـ backend/tests/fixtures/whatsapp/
   - n8n/templates/whatsapp-templates.json ← انقله لـ backend/src/Office.Infrastructure/WhatsApp/Templates/
   - docs/meta-setup.md ← فضّله في مكانه، وهتعدّله في القسم 9.
   - scripts/*/tunnel-refresh* وtunnel-down*: خدهم كمرجع بس. هيتكتبوا من جديد (القسم 7).
4. وقّف n8n وAppsmith لو شغالين. البيانات اللي فيهم بيانات اختبار ومش محتاجينها.
   خد backup الأول (scripts/linux/backup.sh)، واكتب اسم الملف في التقرير.
5. بعد ما تخلص القسم ده، git status لازم يبين بس DECISIONS-v1.1.md
   وTASK-02-backend-prompt.md والملفات اللي رجّعتها من الأرشيف.

==================================================
القسم 1: النطاق
==================================================

المطلوب:
- Solution بـ .NET 10، وهيكل Modular Monolith (القسم 2).
- EF Core + Npgsql، وmigrations، وmigrator منفصل (القسم 3).
- Webhook بيتحقق من التوقيع، والحفظ، والمعالجة في الخلفية بـ Hangfire (القسم 4).
- إرسال رسالة نصية وtemplate، ومعاهم idempotency ونافذة الـ 24 ساعة (القسم 5).
- القوالب: تقديمها لـ Meta ومزامنة حالتها (القسم 5.4).
- أوامر تشغيل (CLI) وتحديث Docker Compose والسكريبتات (القسم 6 و7).
- Logging وhealth checks وتنبيهات Telegram (القسم 8).
- الاختبارات (القسم 10).

خارج النطاق، وممنوع يتعمل هنا:
- أي جدول أو منطق لـ persons، أو matters، أو conversations، أو intake، أو مواعيد، أو دفع.
- تنزيل الميديا، والتجميع (8 ثواني)، وتفريغ الفويس. دول بتوع TASK 03.
- الـ AI والتصنيف والنصوص المعتمدة. دول بتوع TASK 04.
- React واللوحة، وASP.NET Core Identity، وتسجيل الدخول. دول بتوع TASK 07.
  (جهّز بس مكان الـ authentication في الـ pipeline، من غير مستخدمين.)
- نقل رقم المكتب الحالي.

==================================================
القسم 2: هيكل الـ Solution
==================================================

backend/
  Office.sln
  Directory.Build.props       ← net10.0، وNullable enable، وTreatWarningsAsErrors،
                                 وImplicitUsings، وAnalysisLevel latest
  Directory.Packages.props    ← Central Package Management، وكل الإصدارات ثابتة
  global.json                 ← SDK 10 محدد
  src/
    Office.Api/               ← الـ host: endpoints، وDI، وmiddleware، وHangfire server،
                                 والـ health checks، وSerilog
    Office.Core/              ← الـ domain والـ application. مقسوم modules كفولدرات:
                                 WhatsApp/ (entities، وقواعد، وinterfaces، وuse cases)
                                 Common/ (Clock، وResult types، وerrors)
                                 ممنوع يعتمد على EF Core أو ASP.NET Core أو HttpClient.
    Office.Infrastructure/    ← OfficeDbContext، وEF configurations، وMigrations،
                                 وWhatsAppCloudClient (HttpClient)، والـ Telegram notifier،
                                 وتطبيقات الـ interfaces اللي في Core.
    Office.Cli/               ← console فيه أوامر التشغيل (القسم 6)
  tests/
    Office.UnitTests/
    Office.IntegrationTests/
    fixtures/whatsapp/        ← من الأرشيف
  Dockerfile                  ← multi-stage، والـ runtime image بمستخدم non-root

اتجاه الاعتماد: Api ← Infrastructure ← Core، وCli ← Infrastructure ← Core.
Core ما بيعتمدش على حد. وأضف architecture test (NetArchTest أو اختبار reflection بسيط)
يتأكد من ده.

قواعد:
- كل package بإصدار محدد ومنشور من أسبوعين على الأقل، واتأكد من الإصدار على nuget.org
  وقت التنفيذ. ممنوع preview.
- مفيش AutoMapper ولا MediatR ولا مكتبات مش ضرورية. استخدم classes وmethods عادية.
- Minimal APIs، ومعاها route groups لكل module.
- كل الأوقات DateTimeOffset/timestamptz بـ UTC في الداتابيز. والتحويل لتوقيت
  Africa/Cairo بيحصل بس في العرض والحسابات اللي بتعتمد على الساعة (من TimeProvider
  ومعاه helper واحد للتوقيت).
- TimeProvider يتحقن في كل حتة، عشان الاختبارات تتحكم في الوقت.
- البناء والاختبار لازم يشتغلوا كمان من غير .NET SDK على الجهاز: سكريبتات build وtest
  بتشغّل image الـ sdk:10 الرسمي من Docker.

==================================================
القسم 3: قاعدة البيانات
==================================================

3.1 الأدوات
- EF Core + Npgsql.EntityFrameworkCore.PostgreSQL، وEFCore.NamingConventions
  (snake_case)، وكل الجداول في schema app.
- المفاتيح uuid، وقيمها من Guid.CreateVersion7().
- الـ migrations بـ EF Core في Office.Infrastructure/Persistence/Migrations.
- ممنوع Database.Migrate() أو EnsureCreated() في الـ API. الـ migrations بتتطبق بس بأمر
  migrate في Office.Cli، باليوزر office_migrator.
- الـ API بيشتغل بـ office_app (DML بس).
- dbmate وdatabase/migrations/ يتشالوا من الـ repo ومن docker-compose (T-19).
  وأول migration في EF لازم يعمل اللي 0001_base كان بيعمله:
  CREATE EXTENSION IF NOT EXISTS pgcrypto/citext، وschema app يملكها office_migrator،
  وGRANT USAGE ON SCHEMA app TO office_app, office_readonly.
- الـ default privileges من سكريبت الـ init بتاع TASK 01 بتدّي office_app وoffice_readonly
  صلاحيات على أي جدول جديد. اتأكد إنها شغالة على جداول EF، وعلى جدول
  __EFMigrationsHistory (حطه في schema app).

3.2 الجداول (نفس معنى نسخة n8n، ومتصممة كـ entities)
- wa_webhook_events: id، وreceived_at، وfield، وphone_number_id، وpayload (jsonb)،
  وpayload_sha256 (unique)، وsignature_valid (bool)، وprocessing_status
  (received|processed|ignored|failed)، وerror (jsonb)، وprocessed_at.
- wa_inbound_messages: id، وwamid (unique)، وwa_id، وcontact_name، وphone_number_id،
  وmessage_type، وsent_at، وreceived_at، وcontext_wamid، وreferral (jsonb)، وis_group،
  وraw (jsonb)، وwebhook_event_id (FK)، وprocessing_status (pending|processing|processed|
  ignored_group|ignored_unsupported|failed)، وprocessed_at، وupdated_at.
- wa_contact_windows: wa_id (PK)، وlast_inbound_at، وlast_inbound_wamid، وupdated_at.
- wa_outbound_messages: id، وidempotency_key (unique)، وwa_id، وkind (text|template|media)،
  وtemplate_name، وtemplate_language، وrequest_payload، وresponse_payload، وwamid (unique)،
  وstatus (queued|sending|sent|delivered|read|failed|blocked_window)، وattempts،
  وlast_error، وrequested_by، وcreated_at، وsent_at، وupdated_at.
- wa_message_statuses: id، وwamid، وstatus، وstatus_at، وrecipient_id، وerrors، وpricing،
  وraw، وunique(wamid, status, status_at).
- wa_templates: (name, language) PK، وcategory، وstatus، وmeta_template_id، وcomponents،
  وrejection_reason، وlast_synced_at.
- CHECK constraints في الداتابيز على كل عمود status، وindexes على (wa_id, sent_at)،
  و(processing_status, received_at)، و(wa_id, created_at).
- مفيش جدول settings. الإعدادات اللي مش secret في الـ configuration (القسم 4.4).

3.3 Hangfire
- Hangfire.PostgreSql، وschema اسمها hangfire.
- أمر migrate في Office.Cli هو اللي يجهّز schema الـ Hangfire باليوزر office_migrator
  (PrepareSchemaIfNecessary = true هناك بس).
- الـ API بيشتغل بـ PrepareSchemaIfNecessary = false باليوزر office_app.
  ادي office_app الصلاحيات اللي Hangfire محتاجها على schema hangfire، واكتبها.
- Hangfire Dashboard: مقفول في كل البيئات لحد TASK 07. أو مسموح بيه على localhost
  بس في ENVIRONMENT=test، ووثّق اختيارك.

==================================================
القسم 4: استقبال واتساب (T-17)
==================================================

4.1 Endpoints
GET /webhooks/whatsapp
- لو hub.mode=subscribe وhub.verify_token مطابق لـ WhatsApp:VerifyToken:
  يرجع hub.challenge كـ text/plain و200.
- غير كده: 403.

POST /webhooks/whatsapp
1. اقرا الـ raw body (حده الأقصى 1 MB، وأي حاجة أكبر 413) قبل أي model binding.
2. احسب HMAC-SHA256 على الـ raw bytes بـ WhatsApp:AppSecret، وقارنه بـ X-Hub-Signature-256
   بـ CryptographicOperations.FixedTimeEquals.
   - التوقيع غلط أو مش موجود ← 401، ومفيش أي حفظ. يتسجل log تحذير من غير الـ body،
     ويزيد عداد.
3. لو object != whatsapp_business_account ← 200، ومفيش حفظ.
4. لكل change في entry[].changes[]: احفظ صف في wa_webhook_events (value + field،
   والـ sha256 بتاع الـ JSON بعد ما يتعمله canonicalize)، وON CONFLICT DO NOTHING.
5. اعمل enqueue لـ Hangfire job لكل حدث جديد (ProcessWhatsAppEvent(eventId)).
6. رجّع 200 بسرعة. الهدف أقل من 1 ثانية، وممنوع أي استدعاء خارجي جوه الـ request.
- الـ endpoint ده مستثنى من الـ authentication والـ antiforgery، وعليه rate limiting
  بسيط (ASP.NET Core RateLimiter) مش بيأثر على Meta.

4.2 ProcessWhatsAppEvent (Hangfire job، idempotent)
في transaction واحدة:
- الرسائل (messages[]):
  - INSERT في wa_inbound_messages، وON CONFLICT (wamid) DO NOTHING.
  - contact_name من contacts[] حسب wa_id.
  - sent_at من timestamp.
  - referral يتحفظ زي ما هو (B-13).
  - is_group = true لو الرسالة فيها أي إشارة group حسب توثيق Meta الحالي. ساعتها
    processing_status = ignored_group، ومفيش تحديث للنافذة.
  - غير كده: pending، وحدّث wa_contact_windows بـ GREATEST (النافذة ما ترجعش لورا).
  - ممنوع تنزّل الميديا. احتفظ بالـ raw بس.
- الحالات (statuses[]):
  - INSERT في wa_message_statuses، وON CONFLICT DO NOTHING.
  - حدّث wa_outbound_messages.status بالترتيب: queued < sending < sent < delivered < read.
    failed حالة نهائية. status قديم ما يرجعش الحالة لورا.
- field = message_template_status_update ← upsert في wa_templates.
- حدّث processing_status بتاع الحدث. ولو حصل exception: failed، وerror، وHangfire
  retry (3 مرات بـ backoff). بعد آخر محاولة يتبعت تنبيه (القسم 8).
القواعد دي في Core (WhatsApp module)، وتتختبر unit tests، والـ job بس بيناديها.

4.3 مستخدم الـ webhook عند Meta
- الاشتراك بيتسجل بـ Graph API: POST /{app-id}/subscriptions (object
  whatsapp_business_account، وcallback_url، وverify_token، وfields=messages)، بـ app
  access token (app_id|app_secret). ده أمر في Office.Cli (القسم 6)، ومش بيتعمل يدوي.
- اتأكد إن الـ WABA مشترك في الـ App:
  GET/POST /{waba-id}/subscribed_apps (أمر wa-check).
- ارجع لتوثيق Meta وقت التنفيذ، واتأكد من الـ endpoints والحقول.

4.4 الإعدادات (Options pattern + ValidateOnStart)
WhatsApp:
  Enabled               (bool، افتراضي false. لو false، الـ API يشتغل والـ webhook يرد 503)
  AppId, WabaId, PhoneNumberId, GraphApiVersion      ← مش secrets، من env/appsettings
  AppSecret, AccessToken, VerifyToken                ← secrets، من env بس (T-21)
  TestRecipients        (list)
  EchoTest:Enabled      (bool، افتراضي false، ومسموح بس لو Environment = test)
- لو Enabled = true وأي قيمة ناقصة، الـ API يرفض يقوم برسالة واضحة.
- GraphApiVersion يتحدد من Meta changelog وقت التنفيذ. ما تخمنوش.
- أسماء الـ env: WhatsApp__AppSecret وهكذا. ضيفها في .env.example بـ CHANGE_ME
  أو فاضية، وفي docs/env-variables.md.

==================================================
القسم 5: الإرسال
==================================================

5.1 WhatsAppCloudClient
- Typed HttpClient على https://graph.facebook.com/{GraphApiVersion}/.
- Authorization: Bearer AccessToken (من الـ options، وممنوع يتسجل في الـ logs).
- Microsoft.Extensions.Http.Resilience:
  - timeout 15 ثانية.
  - retry 3 مرات بـ exponential backoff + jitter على أخطاء الشبكة و5xx و429 بس.
  - ممنوع retry على 4xx التانية.
- الأخطاء تتحول لـ error codes (اتأكد من الأرقام في توثيق Meta الحالي):
  131047 ← OUTSIDE_24H_WINDOW، و190 ← TOKEN_INVALID، و130429/131056/80007 ← RATE_LIMIT،
  و131026 ← UNDELIVERABLE، و131030 ← RECIPIENT_NOT_ALLOWED، وغير كده META_ERROR،
  ومن غير response NETWORK_OR_UNKNOWN.

5.2 SendTextMessage (use case في Core)
Input: waId، وtext، وidempotencyKey، وrequestedBy، و(اختياري) contextWamid.
1. Validation: waId أرقام من 8 لـ 15. text مش فاضي وحده الأقصى 4096. الـ key والـ
   requestedBy مطلوبين.
2. INSERT في wa_outbound_messages بحالة queued.
   - لو الـ key موجود قبل كده ← رجّع النتيجة القديمة (Duplicate)، ومن غير إرسال.
3. نافذة الـ 24 ساعة (M-02): لو مفيش wa_contact_windows، أو آخر رسالة من 24 ساعة أو
   أكتر ← status = blocked_window، وOUTSIDE_24H_WINDOW، **ومفيش أي request لـ Meta**.
4. status = sending، وattempts + 1، وبعدين الإرسال.
5. نجح ← sent، وwamid، وsent_at. فشل ← failed، وlast_error، والـ error code.
6. Output: { ok, outboundId, wamid, status, errorCode, errorMessage }.

5.3 SendTemplateMessage
- نفس الـ idempotency والتسجيل.
- مفيش شرط النافذة.
- لو الـ template مش APPROVED في wa_templates ← TEMPLATE_NOT_APPROVED، ومن غير إرسال.

5.4 القوالب
- whatsapp-templates.json (من الأرشيف) فيه الـ 11 template بتوع M-03. راجعه على
  DECISIONS-v1.1 وصلّحه لو فيه اختلاف. category UTILITY، واللغة ar، ولكل متغير مثال.
- أمر templates-submit: يبعت أي template مش موجود عند Meta
  (POST /{waba-id}/message_templates). ممنوع يبعته تاني، وممنوع يمسح.
- أمر templates-sync، وHangfire recurring job كل 6 ساعات: يجيب كل القوالب ويحدّث
  wa_templates.
- لو Meta غيّرت category أي template (مثلًا booking_followup بقى MARKETING)، سجّله
  ونبّه عليه في التقرير.

5.5 Echo test (للاختبار بس)
- Hangfire job، أو بعد ProcessWhatsAppEvent. اختار الأبسط ووثّقه.
- بيرد بس لو: EchoTest:Enabled = true، وEnvironment = test، والـ wa_id جوه
  TestRecipients، والرسالة text، وما اتردش عليها قبل كده.
- الرد: «تم استلام رسالتك (اختبار) ✅»، بـ idempotencyKey = "echo:" + wamid.
- ما يغيّرش processing_status بتاع الرسالة.
- افتراضيًا false، وفي آخر التاسك لازم يرجع false.

==================================================
القسم 6: Office.Cli
==================================================

الأوامر (System.CommandLine، أو parsing بسيط):
- migrate [--to <migration>]       ← EF migrations + schema Hangfire، باليوزر office_migrator
- migrations-list
- wa-subscribe --callback <https-url>   ← يسجّل أو يحدّث اشتراك الـ webhook عند Meta
- wa-check                          ← يطبع الاشتراك الحالي والـ callback URL، والـ
                                       subscribed_apps للـ WABA، ورقم التليفون وحالته
- templates-submit
- templates-sync
- send-test --to <wa_id> --text "..."   ← بيرفض أي رقم برا TestRecipients
كل أمر بيطبع النتيجة كـ JSON، وexit code غير صفر عند الفشل.
وكل أمر بيستخدم أقل connection string محتاجها (migrate بس هو اللي بيستخدم الـ migrator).

==================================================
القسم 7: Docker Compose والسكريبتات
==================================================

7.1 Compose
- اشيل service n8n، وappsmith، وmigrate (dbmate)، وvolumes بتاعتهم.
- ضيف:
  api:
    build backend/Dockerfile، وport 127.0.0.1:8081:8080، وdepends_on postgres وredis
    (healthy)، وhealthcheck على /health/ready، وenv من .env (connection string بـ office_app
    وWhatsApp__* وTelegram__*)، وTZ=Africa/Cairo، وASPNETCORE_ENVIRONMENT من ENVIRONMENT.
  cli (profile tools): نفس الـ image بـ entrypoint بتاع Office.Cli. connection strings
    للـ app وللـ migrator.
- الـ tunnel (profile tunnel) يوجّه لـ http://api:8080، ولـ api بس.
- infra/postgres/init: اشيل قاعدة n8n وn8n_user من السكريبت، وخليه idempotent.
  وفي بيئة الاختبار الحالية: DROP DATABASE n8n، وDROP ROLE n8n_user، بعد الـ backup.
- preflight: اشيل متغيرات N8N_*، وضيف التحقق من connection strings الجديدة. ومتغيرات
  WhatsApp__* السرية مطلوبة بس لو WhatsApp__Enabled=true.
- .env.example وdocs/env-variables.md يتحدثوا.

7.2 السكريبتات (PowerShell + Bash، بنفس أسلوب scripts/)
- backend-build، وbackend-test (من غير SDK محلي، بـ image sdk:10)
- migrate ← بقى ينادي cli migrate
- tunnel-refresh:
  1. شغّل profile tunnel، واقرا الرابط.
  2. لو الرابط زي الحالي، اخرج.
  3. نادي cli wa-subscribe --callback <url>/webhooks/whatsapp.
  4. نادي cli wa-check، واتأكد إن الـ callback الجديد اتسجل.
  5. اطبع النتيجة.
  (مفيش restart للـ API، لأن الرابط مش جزء من إعداداته.)
- tunnel-down: يقفل الـ tunnel، ويطبع تنبيه إن Meta مش هتوصل لحد ما tunnel-refresh
  يشتغل تاني.
- healthcheck: اشيل فحوص n8n، وضيف:
  - api /health/ready = 200.
  - __EFMigrationsHistory فيه آخر migration.
  - office_app ما يقدرش يعمل DDL في app.
  - office_readonly ما يقدرش يكتب في wa_*.
  - schema hangfire موجودة.

==================================================
القسم 8: Logging وhealth checks والأمان
==================================================
- Serilog بـ JSON على الـ console.
  - ممنوع تسجيل: نص الرسايل، أو الـ AccessToken، أو AppSecret، أو headers الـ Authorization،
    أو الـ raw body.
  - أرقام التليفونات تتسجل masked، زي 2010****122.
- Global exception handler يرجّع ProblemDetails، من غير stack traces برا Development.
- /health/live و/health/ready (PostgreSQL وRedis).
- تنبيه Telegram (اختياري، لو Telegram__BotToken وTelegram__ChatId موجودين) في الحالات دي:
  - job فشل بعد كل المحاولات.
  - TOKEN_INVALID.
  - signature failures أكتر من حد معين في 5 دقايق.
  من غير بيانات عملاء.
- HTTPS بيجي من الـ tunnel. الـ API ورا proxy، فخلّي ForwardedHeaders متظبطة صح.
- Security headers أساسية، ومفيش CORS لحد TASK 07.

==================================================
القسم 9: دليل Meta (docs/meta-setup.md)
==================================================
عدّل الدليل الموجود (من الأرشيف) عشان يناسب v1.1:
- اشيل الجزء 3 بتاع n8n Credentials.
- بداله: صاحب المشروع يحط القيم بنفسه في .env (WhatsApp__AppSecret،
  وWhatsApp__AccessToken، وWhatsApp__VerifyToken)، ويولّد الـ VerifyToken بـ gen-secrets.
  ويحط القيم اللي مش secret (AppId، وWabaId، وPhoneNumberId) في .env برضه.
- ضيف خطوة: tunnel-refresh بيسجّل الـ webhook أوتوماتيك، ومفيش إدخال يدوي في Meta.
- خلّي باقي الأجزاء (App، ورقم الاختبار، وSystem User، وBusiness Verification، ورقم
  المكتب ممنوع) زي ما هي.
- ممنوع تطلب أي secret في الشات.

==================================================
القسم 10: الاختبارات
==================================================

10.1 Unit tests (Office.UnitTests)
[ ] Signature: صح ← مقبول، وغلط أو ناقص أو بادئة غلط ← مرفوض، والمقارنة constant-time.
[ ] نافذة الـ 24 ساعة: 23:59 مسموح، و24:00 و25 ساعة ممنوع، ومفيش نافذة ممنوع.
    بـ TimeProvider مزيف.
[ ] ترتيب الحالات: sent ثم read ثم delivered متأخر ← read، وfailed نهائي.
[ ] تحويل أخطاء Meta لكل error code.
[ ] canonicalize JSON: نفس المحتوى بترتيب مختلف ← نفس الـ hash.
[ ] Architecture test: Core ما بيعتمدش على Infrastructure ولا ASP.NET.

10.2 Integration tests (Office.IntegrationTests)
- WebApplicationFactory + PostgreSQL حقيقي (Testcontainers، أو قاعدة اختبار منفصلة
  على الـ stack المحلي، ووثّق اختيارك).
- WhatsApp:AppSecret للاختبار بس، والـ fixtures من tests/fixtures/whatsapp. وتتبعت
  للـ endpoint الحقيقي بتوقيع محسوب.
- WhatsAppCloudClient بـ fake HttpMessageHandler بيسجّل الـ requests.
[ ] GET verify: token صح ← challenge، وغلط ← 403.
[ ] POST بتوقيع غلط ← 401، ومفيش صفوف.
[ ] text ← حدث + رسالة pending + نافذة.
[ ] نفس الـ payload مرتين ← صف واحد.
[ ] نفس الـ wamid في حدث تاني ← رسالة واحدة.
[ ] audio وimage وdocument بأنواعهم، والميديا ما اتنزلتش.
[ ] referral اتحفظ.
[ ] group ← ignored_group، ومفيش نافذة.
[ ] statuses بالترتيب ← read، وfailed فيه الخطأ.
[ ] template status update ← wa_templates اتحدث.
[ ] SendText لرقم آخر رسالة منه من 25 ساعة ← blocked_window، و**صفر requests** في الـ fake handler.
[ ] SendText بنفس الـ key مرتين ← request واحد.
[ ] SendTemplate لـ template مش approved ← TEMPLATE_NOT_APPROVED، وصفر requests.
[ ] Meta رجّعت 190 ← TOKEN_INVALID، والتنبيه اتنادى (fake notifier).
[ ] الـ webhook بيرد في أقل من 1 ثانية (الـ job بيتنفذ بعدين).
[ ] office_app (connection الـ API) ما يقدرش يعمل DDL.

10.3 اختبارات حقيقية مع Meta (بعد ما صاحب المشروع يكمّل docs/meta-setup.md)
[ ] tunnel-refresh سجّل الـ webhook، وwa-check بيبين الـ callback الجديد.
[ ] رسالة نصية من رقم الـ recipient ← اتسجلت مرة واحدة.
[ ] فويس وصورة ← اتسجلوا بالنوع الصح.
[ ] Echo test مفعّل ← رد على رقم مسموح بيه بس، والـ statuses وصلت (sent وdelivered وread).
[ ] send-test لرقم برا القائمة ← مرفوض.
[ ] template (hello_world أو واحد approved) اتبعت.
[ ] POST بتوقيع غلط على الرابط العام ← 401.
[ ] دورة tunnel-refresh اتجربت مرتين، ورسالة وصلت بعد كل مرة.
[ ] templates-submit للـ 11، وtemplates-sync جاب حالتهم.
لو القيم من Meta لسه ما وصلتش، كمّل كل حاجة تانية، واكتب بالظبط اللي ناقص.

==================================================
القسم 11: التوثيق
==================================================
- backend/README.md: الهيكل، وإزاي تبني وتختبر (بـ SDK وبغيره)، وإزاي تضيف module.
- docs/architecture.md: Mermaid diagram (Meta ← tunnel ← API ← PostgreSQL/Redis،
  وHangfire، وCLI)، واتجاه الاعتماد، وقواعد الـ modules.
- docs/whatsapp-integration.md: الـ endpoints، والـ jobs، والـ use cases ومدخلاتها
  ومخرجاتها، وجدول error codes، ونافذة الـ 24 ساعة، والـ templates.
- docs/runbooks/whatsapp.md: الـ token انتهى، ورابط الـ tunnel اتغير، وMeta مش بتبعت،
  وtemplate اترفض، وrate limit، وsignature failures، وخطة نقل رقم المكتب (توثيق بس).
- حدّث README.md (n8n وAppsmith اتشالوا، والـ api اتضاف، وجدول الخدمات)،
  وdocs/infrastructure.md، وdocs/env-variables.md.

==================================================
القسم 12: شروط القبول
==================================================
[ ] القسم 0 اتنفذ: branch الأرشيف موجود، وmaster نضيف.
[ ] dotnet build من غير warnings، وكل الـ unit tests والـ integration tests PASS
    (من backend-test، ومن غير SDK محلي).
[ ] cli migrate على قاعدة فاضية ← كل الجداول وschema hangfire اتعملوا، وتشغيله مرتين
    ما بيعملش حاجة تانية.
[ ] الـ API بيشتغل بـ office_app، وhealth/ready = 200، وWhatsApp:Enabled=false ما بيوقعش
    الـ API.
[ ] n8n وAppsmith وdbmate وقاعدة n8n اتشالوا، والـ healthcheck كله PASS.
[ ] reset ثم up ثم migrate ثم healthcheck من الصفر شغال.
[ ] مفيش secret في git، ولا في الـ logs (دوّر في output الاختبارات والـ container logs
    على قيم .env).
[ ] اختبارات 10.3 PASS، أو مكتوب بالظبط اللي مستني صاحب المشروع.
[ ] EchoTest:Enabled = false في الآخر.
[ ] رقم المكتب ما اتلمسش.
[ ] commit واحد أو أكتر على master برسائل واضحة، وآخر سطر فيها:
    Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
    (من غير push).

==================================================
القسم 13: التقرير النهائي
==================================================
1. نتيجة القسم 0: الـ branch، والملفات، واسم ملف الـ backup.
2. إصدارات .NET SDK وكل الـ packages.
3. الملفات اللي اتعملت أو اتعدلت.
4. الجداول، والـ endpoints، والـ jobs، والأوامر.
5. نتيجة كل اختبار + الدليل.
6. حالة Meta، وحالة كل template.
7. أي اختلاف عن البرومت أو عن DECISIONS-v1.1، والسبب.
8. المخاطر والقيود.
9. اللي TASK 03 محتاجه: wa_inbound_messages وحالة pending، والـ use cases بتاعة
   الإرسال، وأماكن إضافة modules جديدة.

==================================================
ممنوعات نهائية
==================================================
- ممنوع تمسح شغل نسخة n8n من غير ما يتحفظ على branch الأرشيف.
- ممنوع تدخل على حساب Meta أو Facebook، أو تطلب secrets في الشات، أو تكتبها في أي ملف
  غير .env (و.env ممنوع يدخل git).
- ممنوع Database.Migrate() في الـ API، وممنوع الـ API يشتغل بيوزر عنده DDL.
- ممنوع تبعت لأي رقم برا TestRecipients.
- ممنوع تنقل رقم المكتب أو تربطه.
- ممنوع أي منطق لحازم، أو AI، أو لوحة، أو تنزيل ميديا.
- ممنوع packages preview، أو مكتبات مش ضرورية.
- ممنوع تقول إن حاجة اشتغلت من غير ما تشغلها فعلًا وتعرض الدليل.
- ممنوع push.
```

---

## ملاحظات ليك (مش للـ agent)

- **الشغل اللي اتعمل بـ n8n مش هيضيع:** هيتحفظ على branch اسمه `archive/task02-n8n`. منه هناخد الـ fixtures، وملف الـ 11 template، ودليل Meta.
- **دليل Meta:** الجزء 1 و2 زي ما هم، فلو كنت بدأت فيهم كمّل عادي. الفرق الوحيد إن الـ App Secret والـ Token هيتحطوا في `.env` على جهازك بدل n8n.
- **الـ webhook من غير n8n:** `tunnel-refresh` هيسجّل الرابط الجديد عند Meta أوتوماتيك. مش هتحتاج تدخل على Meta كل مرة.
- **.NET SDK:** الـ agent هيبني ويختبر من Docker، فمش لازم تسطّب حاجة. لكن لو هتكتب كود بنفسك، سطّب .NET 10 SDK وVisual Studio أو Rider.
