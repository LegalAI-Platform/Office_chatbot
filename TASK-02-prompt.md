# TASK 02 — ربط Meta WhatsApp Cloud API

**الاعتماديات:** TASK 00 (`docs/decisions/DECISIONS-v1.0.md`) + TASK 01 (commit `e7b82e2`)
**البيئة:** Test/Staging فقط
**التاسك اللي بعده:** TASK 03 — الرسائل والميديا وتجميع الـ 8 ثواني

> انسخ كل اللي جوه الـ code block وابعته للـ agent. الـ agent شغال جوه الـ repository نفسه، وكل الملفات المذكورة موجودة.

---

```text
أنت Senior Integration Engineer متخصص في n8n وMeta WhatsApp Cloud API.

مهمتك ربط Meta WhatsApp Cloud API بـ n8n في بيئة الاختبار لنظام واتساب لمكتب
محاماة («مكتب الأستاذ سعد فتحي سعد»). المساعد الآلي اسمه «حازم».
التاسك ده بيبني «السباكة» بس: استقبال الأحداث من Meta، والتحقق منها، وحفظها،
وإرسال الرسائل، وتتبع حالتها، وقوالب Meta. مفيش أي منطق خاص بحازم في التاسك ده.

المراجع، واقراها كلها قبل ما تبدأ:
- docs/decisions/DECISIONS-v1.0.md: المرجع الوحيد للقرارات. لو حاجة في البرومت ده
  اتعارضت معاه، هو اللي يكسب، واذكر التعارض.
- README.md وdocs/infrastructure.md وdocs/env-variables.md وn8n/credentials-docs/README.md:
  البنية اللي اتعملت في TASK 01.
- docker-compose.yml وinfra/ وdatabase/migrations/ وscripts/.

==================================================
القسم 1: الوضع الحالي (اتحقق منه بنفسك، ما تفترضهوش)
==================================================

من TASK 01:
- n8n 2.43.8 في Regular mode، على PostgreSQL، بتوقيت Africa/Cairo، على
  http://127.0.0.1:5678. حساب الـ owner متعمل.
- قاعدة office: schema اسمها app، ومعاها function اسمها app.set_updated_at().
  migration 0001_base متطبق.
- اليوزرز:
  - office_migrator: هو اللي بيعمل DDL، ومن خلال dbmate بس
    (docker compose run --rm migrate up).
  - office_app: DML بس. ده اللي n8n هيستخدمه.
  - office_readonly: قراءة بس.
  - أي جدول office_migrator بيعمله بياخد صلاحيات office_app وoffice_readonly أوتوماتيك.
- N8N_BLOCK_ENV_ACCESS_IN_NODE=true: لا الـ Code nodes ولا الـ expressions يقدروا
  يقروا متغيرات البيئة.
- الـ tunnel: profile اسمه tunnel (Quick Tunnel، رابط بيتغير مع كل تشغيل)، وprofile
  اسمه tunnel-named (رابط ثابت، محتاج CLOUDFLARE_TUNNEL_TOKEN). الـ tunnel بيعرض n8n بس.
- متغير WEBHOOK_URL في .env بيروح لـ n8n باسم N8N_WEBHOOK_URL.
- Garage (S3) شغال، بس التاسك ده مش هيستخدمه.

عند Meta (من DECISIONS-v1.0، M-01):
- Meta Business Account: موجود.
- Business Verification: لسه.
- Meta App: لسه.
- WABA: لسه.
- Display Name: لسه. الاسم المطلوب «مكتب الأستاذ سعد فتحي سعد».
- System User + Token: لسه.
- الرقم: رقم المكتب الحالي هو اللي هيتنقل في الآخر.

ابدأ بالتأكد إن الـ stack شغال وإن scripts/linux/healthcheck.sh (أو نسخة الـ .ps1)
كله PASS. لو فيه حاجة فاشلة، وقف واكتبها في التقرير.

==================================================
القسم 2: قرارات التصميم في التاسك ده
==================================================

D-1  الاستقبال بـ n8n «WhatsApp Trigger» node (n8n-nodes-base.whatsAppTrigger)،
     مش Webhook node عادي.
     السبب، واتأكد منه في الكود المتسطب في الـ container:
     - بيتحقق من X-Hub-Signature-256 بـ HMAC-SHA256 على الـ raw body، وبيستخدم الـ App
       Secret من credential مشفّرة (whatsAppTriggerApi)، وبيرفض أي توقيع غلط.
     - بيرد على GET verification لوحده، والـ verify token بيبقى id الـ node نفسه.
     - أول ما الـ workflow يتفعّل بيسجّل الـ webhook subscription على الـ Meta App لوحده.
     ده بيحقق قاعدة «الـ secrets في n8n Credentials بس» من غير ما نفتح env access
     ولا نكتب الـ App Secret في workflow JSON.
     القيود اللي لازم تتوثق:
     - Meta App واحد ليه callback URL واحد بس. بيئة الإنتاج بعدين هتحتاج Meta App
       منفصل، أو هتسجّل الـ subscription من جديد.
     - الأحداث اللي توقيعها غلط بتتجاهل بصمت، ومش بتطلع execution.
     الكود موجود هنا جوه الـ container:
     /usr/local/lib/node_modules/n8n/node_modules/.pnpm/n8n-nodes-base*/node_modules/
     n8n-nodes-base/dist/nodes/WhatsApp/WhatsAppTrigger.node.js
     لو لقيت سلوكه مختلف عن الكلام ده في النسخة المتسطبة، وقف واسأل.

D-2  الإرسال بـ HTTP Request node على Graph API، ومعاه credential من نوع HTTP Header Auth
     (Authorization: Bearer <system user token>)، مش بـ WhatsApp node.
     السبب: تحكم كامل في الـ payload والـ retry، وتسجيل الـ wamid والأخطاء.

D-3  الإعدادات اللي مش secret (phone_number_id، waba_id، graph_api_version،
     test_recipients، ...) تتحفظ في جدول app.settings، وتتقري بـ Postgres node، وكل
     الـ workflows تجيبها من sub-workflow واحد اسمه CFG-GET.
     ممنوع تتكتب hardcoded في الـ nodes. وممنوع نفتح env access.

D-4  الاختبار الأول على رقم الاختبار المجاني من Meta (Test phone number اللي بيظهر في
     الـ App Dashboard، ومعاه لحد 5 أرقام مستقبِلة)، مش على رقم المكتب.
     السبب: حازم لسه مش مبني، وأي رد غلط هيروح لعملاء حقيقيين.
     نقل رقم المكتب الحالي (Coexistence أو Migration) مذكور في القسم 3.4، وممنوع
     يتنفذ في التاسك ده من غير موافقة صريحة من صاحب المشروع.

D-5  الـ webhook بيشتغل على Quick Tunnel (قرار صاحب المشروع، لأن مفيش دومين).
     الرابط https://*.trycloudflare.com بيتغير مع كل restart للـ tunnel، وده بيحصل
     كمان لو Docker Desktop أو الجهاز عمل restart. والـ trigger بيسجّل الـ callback URL
     عند Meta بس وقت تفعيل الـ workflow.
     لذلك مطلوب:
     a) سكريبت tunnel-refresh (PowerShell + Bash، بنفس أسلوب scripts/) يعمل الآتي
        بالترتيب، ويقف عند أول خطأ:
        1. يتأكد إن الـ owner متعمل (showSetupOnFirstLoad = false). لو مش متعمل، يرفض.
        2. يشغّل profile tunnel لو مش شغال، ويقرا الرابط الجديد من الـ logs.
        3. لو الرابط زي WEBHOOK_URL الحالي، يقول كده ويخرج.
        4. يحدّث WEBHOOK_URL=<url>/ وN8N_SECURE_COOKIE=true في .env. السطرين دول
           بس، ومن غير ما يلمس أي secret. ويعمل نسخة احتياطية من .env الأول.
        5. يعمل restart لـ n8n ويستنى /healthz/readiness.
        6. يعمل unpublish ثم publish لـ META-WA-INBOUND، عشان الـ trigger يسجّل
           الـ subscription على الرابط الجديد. اتأكد بنفسك من الطريقة المتاحة في
           n8n 2.43.8: CLI جوه الـ container، أو n8n Public API بـ API key متخزن برا
           git. استخدم الأبسط اللي شغال فعلًا ووثّقه. لو مفيش طريقة أوتوماتيك
           موثوقة، السكريبت يطبع الخطوة اليدوية بالظبط ويستنى تأكيد.
        7. يتأكد إن Meta شايلة الرابط الجديد (GET /{app_id}/subscriptions بـ app
           access token أو بالطريقة المتاحة)، أو على الأقل يتأكد إن رسالة اختبار
           جديدة وصلت wa_webhook_events.
        8. يطبع الرابط الجديد، ووقت التحديث، والنتيجة.
     b) سكريبت tunnel-down: يقفل الـ tunnel، ويرجّع WEBHOOK_URL=http://127.0.0.1:5678/
        وN8N_SECURE_COOKIE=false، ويعمل restart لـ n8n. ويطبع تنبيه إن Meta مش
        هتقدر توصل للـ webhook لحد ما tunnel-refresh يشتغل تاني.
     c) أي حدث يوصل وقت ما الرابط القديم ميت بيضيع منّا. Meta بتعيد المحاولة لفترة
        محدودة، فاتأكد من المدة الحالية في توثيقها. وثّق ده كقيد في docs/runbooks/whatsapp.md.
     d) جرّب الدورة كاملة مرتين فعليًا: restart للـ tunnel ← tunnel-refresh ← رسالة
        اختبار من رقم الـ recipient توصل وتتسجل.
     e) قيد للإنتاج: لازم دومين ورابط ثابت (T-13). الـ Quick Tunnel للاختبار بس.

D-6  التاسك ده ما بيعملش جداول persons أو conversations أو matters. دي بتاعة TASK 03
     وTASK 05. الرسائل الواردة تتحفظ في app.wa_inbound_messages بحالة pending،
     وTASK 03 هو اللي هيكمل معالجتها.

==================================================
القسم 3: تجهيزات Meta (يدوي، صاحب المشروع هو اللي يعملها)
==================================================

ممنوع تدخل على حساب Meta أو Facebook بتاع صاحب المشروع، أو تعمل عليه أي حاجة.
مطلوب منك تكتب docs/meta-setup.md: دليل خطوة بخطوة بالعربي، ومعاه checklist.
اكتب الدليل، وبعدين وقف واستنى صاحب المشروع يكمّل الخطوات ويبعتلك القيم اللي مش
secret. الـ secrets هو بيدخلها بنفسه في n8n Credentials، ومش هيبعتهالك.

3.1  الـ App والـ WABA (للاختبار)
     1. developers.facebook.com → Create App → Business، مربوط بالـ Business Account.
     2. يضيف WhatsApp product. Meta بتعمل WABA تجريبي ومعاه Test phone number.
     3. يضيف رقمه الشخصي كـ recipient في «To»، ويأكده بالكود اللي بيوصله.
     4. يسجّل القيم دي (مش secrets): App ID، وWABA ID، وPhone Number ID بتاع رقم
        الاختبار، ورقم الاختبار نفسه، ورقم/أرقام الـ recipients.
     5. App Secret: من App settings → Basic. يدخله صاحب المشروع في n8n credential
        «meta-whatsapp-trigger» بس.

3.2  System User و Permanent Token
     1. Business Settings → Users → System users → Add (Admin).
     2. Add assets: الـ App (Full control) والـ WABA (Full control).
     3. Generate token، والصلاحيات: whatsapp_business_messaging،
        whatsapp_business_management، business_management. الـ expiration: Never.
     4. صاحب المشروع يدخله في n8n credential «meta-whatsapp-token» بس
        (HTTP Header Auth، والـ Name: Authorization، والـ Value: Bearer <token>).
     5. وثّق إزاي يتعمل token جديد (rotation)، وإيه اللي بيحصل لو الـ token اتلغى
        (Meta error 190).

3.3  Business Verification و Display Name
     - Business Verification ضروري قبل أي رقم حقيقي، وقبل ما الـ limits ترتفع.
     - الـ Display Name «مكتب الأستاذ سعد فتحي سعد» لازم Meta توافق عليه، ولازم
       يطابق اسم البيزنس الموثّق.
     - وثّق الخطوات والمستندات المطلوبة، وإن ده ممكن ياخد أيام.

3.4  نقل رقم المكتب الحالي (توثيق بس، ممنوع التنفيذ في التاسك ده)
     - Coexistence: الرقم يفضل شغال على تطبيق WhatsApp Business ويتربط بالـ Cloud API.
       وثّق الشروط والقيود، ووثّق إزاي تتأكد إنها متاحة للرقم ده في مصر.
     - البديل: فك الرقم من التطبيق، ونقله للـ Cloud API. المحادثات القديمة مش هتتنقل.
     - في الحالتين لازم backup لمحادثات الواتساب الحالية الأول.
     - اكتب خطة النقل كـ checklist منفصلة. التنفيذ بقرار من صاحب المشروع بعد ما
       حازم يتبني ويتجرب.

3.5  Graph API version
     - شوف آخر نسخة stable من Graph API في Meta changelog وقت التنفيذ، واكتبها في
       app.settings.graph_api_version. ما تخمنش رقم.

==================================================
القسم 4: قاعدة البيانات (migration 0002_whatsapp.sql)
==================================================

migration واحد في database/migrations، على نفس شكل 0001 (-- migrate:up / -- migrate:down)،
وكل الجداول في schema app. id من نوع uuid افتراضيه gen_random_uuid().
created_at وupdated_at من نوع timestamptz، وupdated_at عليه trigger بـ
app.set_updated_at() لأي جدول بيتعدل.

4.1  app.settings
     key text PK, value text NOT NULL, description text, updated_at, updated_by text.
     ممنوع يتحفظ فيه أي secret. ضيف CHECK أو comment بيوضح ده.
     Seed في الـ migration بمفاتيح من غير قيم حقيقية:
       whatsapp.phone_number_id = ''         (من القسم 3.1)
       whatsapp.waba_id = ''
       whatsapp.graph_api_version = ''       (القسم 3.5)
       whatsapp.test_recipients = ''         (أرقام wa_id مفصولة بفاصلة)
       whatsapp.echo_test_enabled = 'false'
     القيم الحقيقية تتحط بعدين بأمر SQL موثّق، أو من سكريبت.

4.2  app.wa_webhook_events
     حدث واحد = change value واحدة جاية من الـ trigger.
     id, received_at, field text, phone_number_id text,
     payload jsonb NOT NULL, payload_sha256 text UNIQUE NOT NULL,
     processing_status text NOT NULL DEFAULT 'received'
       CHECK (processing_status IN ('received','processed','ignored','failed')),
     error jsonb, processed_at.
     index على (processing_status, received_at).

4.3  app.wa_inbound_messages
     id, wamid text UNIQUE NOT NULL, wa_id text NOT NULL, contact_name text,
     phone_number_id text NOT NULL, message_type text NOT NULL,
     sent_at timestamptz NOT NULL (من message.timestamp)، received_at timestamptz NOT NULL DEFAULT now(),
     context_wamid text, referral jsonb, is_group boolean NOT NULL DEFAULT false,
     raw jsonb NOT NULL, webhook_event_id uuid REFERENCES app.wa_webhook_events,
     processing_status text NOT NULL DEFAULT 'pending'
       CHECK (processing_status IN ('pending','processing','processed','ignored_group','ignored_unsupported','failed')),
     processed_at.
     indexes: (wa_id, sent_at)، و(processing_status, received_at).
     ملاحظة: referral هو مصدر العميل لو جاي من إعلان Click-to-WhatsApp (B-13).
     اتحفظ زي ما هو، وTASK 05 هيستخدمه.

4.4  app.wa_contact_windows
     wa_id text PK, last_inbound_at timestamptz NOT NULL, last_inbound_wamid text, updated_at.
     بيتحدث مع كل رسالة واردة، ولو جت رسالة أقدم من اللي متسجلة ما يرجعش لورا
     (GREATEST). ده اللي بيحدد نافذة الـ 24 ساعة (M-02).

4.5  app.wa_outbound_messages
     id, idempotency_key text UNIQUE NOT NULL, wa_id text NOT NULL,
     kind text NOT NULL CHECK (kind IN ('text','template','media')),
     template_name text, template_language text,
     request_payload jsonb NOT NULL, response_payload jsonb,
     wamid text UNIQUE, status text NOT NULL DEFAULT 'queued'
       CHECK (status IN ('queued','sending','sent','delivered','read','failed','blocked_window')),
     attempts int NOT NULL DEFAULT 0, last_error jsonb,
     requested_by text NOT NULL,      -- اسم الـ workflow أو المستخدم
     created_at, sent_at, updated_at.
     index على (wa_id, created_at).

4.6  app.wa_message_statuses
     id, wamid text NOT NULL, status text NOT NULL, status_at timestamptz NOT NULL,
     recipient_id text, errors jsonb, pricing jsonb, raw jsonb NOT NULL,
     UNIQUE (wamid, status, status_at).

4.7  app.wa_templates
     name text, language text, category text, status text, meta_template_id text,
     components jsonb, rejection_reason text, last_synced_at timestamptz,
     PRIMARY KEY (name, language).

4.8  app.workflow_errors
     id, occurred_at, workflow_name text, execution_id text, node_name text,
     message text, details jsonb, notified boolean NOT NULL DEFAULT false.

لازم:
- migrate up ثم down ثم up يشتغلوا من غير مشاكل.
- office_app يقدر يعمل SELECT/INSERT/UPDATE على الجداول دي، وما يقدرش يعمل DDL.
- ضيف فحوص للجداول الجديدة في infra/tools/bin/healthcheck (وجودها، وإن office_app
  يقدر يكتب فيها، وإن office_readonly ما يقدرش).

==================================================
القسم 5: n8n Credentials (صاحب المشروع بيعملها في الـ UI)
==================================================

حدّث n8n/credentials-docs/README.md بالـ credentials دي:

| الاسم | النوع | المحتوى |
|---|---|---|
| office-postgres-app | Postgres | host postgres، db office، user office_app، password من .env |
| meta-whatsapp-trigger | WhatsApp OAuth API (whatsAppTriggerApi) | Client ID = App ID، Client Secret = App Secret |
| meta-whatsapp-token | HTTP Header Auth | Authorization: Bearer <system user token> |
| ops-telegram (اختياري) | Telegram | bot token. لو مش موجود، الـ error handler يسجل في DB بس |

- ممنوع أي secret يتكتب في workflow JSON، أو في ملف، أو في الـ logs، أو في الشات.
- الـ workflows بتشاور على الـ credentials بالاسم.
- اكتب دليل قصير بالصور أو بالخطوات لعمل الـ credentials دي من n8n UI.

==================================================
القسم 6: الـ Workflows
==================================================

كل workflow يتعمل في n8n، ويتعمله export في n8n/workflows/<NAME>.json
(n8n export:workflow جوه الـ container). ضيف سكريبت import/export في scripts/
(PowerShell وBash) بنفس أسلوب TASK 01.
لكل workflow:
- Settings → Error workflow = META-WA-ERROR-HANDLER.
- Timezone = Africa/Cairo.
- Sticky note بتشرح الـ workflow بيعمل إيه.

6.1  CFG-GET (sub-workflow)
     - Input: مفيش. Output: object فيه كل مفاتيح app.settings.
     - لو مفتاح مطلوب فاضي (phone_number_id أو graph_api_version)، يرجع error واضح،
       مش قيمة فاضية.

6.2  META-WA-INBOUND
     الـ trigger:
     - WhatsApp Trigger. الـ updates: messages (ده بييجي معاه statuses). ضيف
       message_template_status_update لو متاح في الـ node.
     - messageStatusUpdates = all.
     بعد الـ trigger: Execute Workflow → META-WA-PROCESS-EVENT لكل event.
     الـ workflow ده يفضل خفيف، ومفيش فيه أي منطق.

6.3  META-WA-PROCESS-EVENT (sub-workflow، قابل للاختبار من غير Meta)
     Input: event واحد بنفس شكل output الـ trigger (change value + field).
     الخطوات:
     1. احسب payload_sha256، واعمل INSERT في wa_webhook_events بـ
        ON CONFLICT (payload_sha256) DO NOTHING. لو الحدث موجود قبل كده، وقّف
        (duplicate، ومفيش side effects).
     2. لو الحدث فيه messages[]:
        - لكل رسالة: INSERT في wa_inbound_messages بـ ON CONFLICT (wamid) DO NOTHING.
        - message_type ياخد نوع الرسالة من Meta زي ما هو (text, audio, image,
          document, video, sticker, location, contacts, interactive, button,
          reaction, unsupported, ...).
        - لو الرسالة جاية من جروب (أي إشارة لـ group في الـ payload حسب توثيق Meta
          الحالي)، يبقى is_group = true، وprocessing_status = 'ignored_group'، ومفيش رد خالص.
        - حدّث wa_contact_windows (للرسائل الفردية بس).
        - احفظ referral لو موجود.
        - ممنوع تنزّل الميديا هنا. الميديا بتاعة TASK 03. احفظ media id والـ mime_type
          بس، وده جوه raw.
     3. لو الحدث فيه statuses[]:
        - INSERT في wa_message_statuses بـ ON CONFLICT DO NOTHING.
        - حدّث wa_outbound_messages.status بالترتيب الصح بس:
          sent < delivered < read، وfailed حالة نهائية.
          يعني status قديم ما يرجّعش رسالة وصلت لـ sent.
        - لو status = failed، احفظ errors في last_error.
     4. لو field = message_template_status_update، حدّث wa_templates.
     5. حدّث processing_status بتاع الحدث لـ processed أو ignored أو failed.
     كل الكتابة في الداتابيز بـ office-postgres-app، وبـ parameterized queries بس،
     ممنوع تركيب SQL من نصوص جاية من العميل.

6.4  META-WA-SEND-MESSAGE (sub-workflow)
     Input: wa_id, text, idempotency_key, requested_by, (اختياري) context_wamid.
     الخطوات:
     1. Validation:
        - wa_id أرقام بس.
        - text مش فاضي، وحده الأقصى 4096 حرف (حد Meta). قاعدة B-21 (300/600 حرف)
          مسؤولية TASK 04، مش التاسك ده.
     2. INSERT في wa_outbound_messages بحالة queued، وON CONFLICT (idempotency_key)
        DO NOTHING RETURNING. لو الرسالة اتبعتت قبل كده، رجّع النتيجة القديمة من
        غير ما تبعت تاني.
     3. نافذة الـ 24 ساعة (M-02):
        لو مفيش wa_contact_windows، أو now() - last_inbound_at >= 24 ساعة:
        status = 'blocked_window'، ويرجع error code: OUTSIDE_24H_WINDOW.
        في الحالة دي ممنوع تكلم Meta خالص. اللي نادى الـ workflow هو اللي يستخدم template.
     4. HTTP Request:
        - POST https://graph.facebook.com/{graph_api_version}/{phone_number_id}/messages
        - credential: meta-whatsapp-token.
        - timeout 15 ثانية. retry 3 مرات مع backoff على أخطاء الشبكة و5xx و429 بس.
          ممنوع retry على 4xx التانية.
     5. نجح ← status = 'sent'، واحفظ الـ wamid والـ response وsent_at.
        فشل ← status = 'failed'، واحفظ last_error، وattempts.
     6. Output موحد:
        { ok, outbound_id, wamid, status, error_code, error_message }
     أخطاء Meta اللي لازم يتعامل معاها بوضوح (اتأكد من الأرقام في توثيق Meta الحالي):
     - 131047 (re-engagement / برا النافذة) ← error_code OUTSIDE_24H_WINDOW.
     - 131026 (undeliverable).
     - 130429 أو 131056 (rate limit) ← retry.
     - 190 (token expired أو invalid) ← error_code TOKEN_INVALID، وتنبيه فوري.

6.5  META-WA-SEND-TEMPLATE (sub-workflow)
     Input: wa_id, template_name, language (افتراضي ar), components/parameters,
     idempotency_key, requested_by.
     - نفس منطق idempotency والتسجيل والـ retry اللي في 6.4.
     - مفيش شرط نافذة 24 ساعة.
     - لو الـ template مش approved في wa_templates، ارفض بـ TEMPLATE_NOT_APPROVED
       قبل ما تبعت.

6.6  META-WA-TEMPLATES
     الملف n8n/templates/whatsapp-templates.json فيه الـ 11 template بتوع M-03 من
     DECISIONS-v1.0، بالنص والكود زي ما هم.
     - category UTILITY، واللغة ar.
     - لكل متغير {{n}} مثال (example) عشان Meta توافق.
     الـ workflow ليه وضعين (manual trigger):
     a) submit: يبعت أي template مش موجود عند Meta على
        POST /{waba_id}/message_templates.
        ممنوع يبعته تاني لو موجود، وممنوع يمسح أي template.
     b) sync: يجيب كل الـ templates من Meta، ويحدّث wa_templates.
     ضيف كمان schedule كل 6 ساعات للـ sync.
     اكتب في التقرير حالة كل template: APPROVED أو PENDING أو REJECTED، والسبب.
     تحذير: Meta ممكن تصنّف booking_followup («لسه حابب تحجز مع الأستاذ؟») كـ
     MARKETING مش UTILITY. سجّل التصنيف النهائي، ولو اتغير نبّه عليه في التقرير.

6.7  META-WA-ERROR-HANDLER
     - Error Trigger.
     - INSERT في app.workflow_errors: اسم الـ workflow، والـ execution id، والـ node،
       والرسالة، وdetails من غير أي secret ولا headers.
     - لو credential ops-telegram موجودة: يبعت رسالة قصيرة للمسؤول ويحدّث notified.
       ممنوع الرسالة يبقى فيها بيانات عميل، يعني مفيش نص رسائل ولا أرقام كاملة.
       الرقم يتعمله mask زي 2010****122.
     - لو مفيش Telegram: يسجل بس، ويتكتب ده في التقرير.

6.8  WA-ECHO-TEST (للاختبار بس)
     - Schedule كل دقيقة، أو Execute Workflow بعد PROCESS-EVENT، اختار الأبسط ووثّقه.
     - بيرد بس لو:
       - whatsapp.echo_test_enabled = 'true'،
       - والـ wa_id جوه whatsapp.test_recipients،
       - والرسالة text،
       - وما اتردش عليها قبل كده.
     - الرد: «تم استلام رسالتك (اختبار) ✅»، من خلال META-WA-SEND-MESSAGE، والـ
       idempotency_key = 'echo:' + wamid.
     - ما يغيّرش processing_status بتاع الرسالة. ده شغل TASK 03.
     - افتراضيًا echo_test_enabled = false. اختبره وبعدين رجّعه false، واكتب ده في التقرير.

==================================================
القسم 7: الأمان
==================================================

- الـ secrets الـ 3 (App Secret، وSystem User Token، وTelegram token) في n8n
  Credentials بس.
- ممنوع تفتح env access، وممنوع تعدّل N8N_BLOCK_ENV_ACCESS_IN_NODE.
- الـ execution data في n8n فيها أرقام ورسائل عملاء:
  - Save successful executions: خليه للـ errors بس، أو بمدة قصيرة لـ workflows
    الإرسال. وثّق اختيارك.
  - الـ pruning (14 يوم) شغال من TASK 01.
- ممنوع تحط أي رسالة أو رقم عميل حقيقي في fixtures أو logs أو docs.
- الـ tunnel يفضل بيعرض n8n بس.
- قبل ما تشغل الـ tunnel، اتأكد إن الـ owner متعمل
  (/rest/settings → userManagement.showSetupOnFirstLoad = false).

==================================================
القسم 8: الاختبارات
==================================================

8.1  اختبارات من غير Meta (fixtures)
     اعمل fixtures في n8n/fixtures/whatsapp/*.json فيها payloads مزيفة بأرقام وهمية،
     على شكل output الـ trigger: text، وaudio، وimage، وdocument، وreferral، و
     statuses (sent/delivered/read/failed)، وtemplate status update، وgroup.
     وسكريبت (PowerShell + Bash) يشغّل META-WA-PROCESS-EVENT عليهم ويتأكد من
     النتيجة في الداتابيز. المقبول: n8n CLI (execute)، أو webhook داخلي للاختبار
     بس متاح على 127.0.0.1، أو أي طريقة أبسط تختارها وتوثّقها.
     لازم يتأكد من:
     [ ] text event ← صف واحد في wa_webhook_events، وصف في wa_inbound_messages،
         وwa_contact_windows اتحدث.
     [ ] نفس الـ event مرتين ← مفيش صفوف مكررة.
     [ ] نفس الـ wamid في event مختلف ← مفيش رسالة مكررة.
     [ ] group ← ignored_group، ومفيش رد.
     [ ] referral اتحفظ.
     [ ] status sent ثم read ثم delivered (متأخر) ← الحالة النهائية read.
     [ ] status failed ← last_error اتحفظ.
     [ ] SEND-MESSAGE لـ wa_id آخر رسالة منه من 25 ساعة ← blocked_window،
         ومفيش request راح لـ Meta (اتأكد من مفيش HTTP execution أو mock).
     [ ] SEND-MESSAGE بنفس الـ idempotency_key مرتين ← request واحد بس.
     [ ] SEND-TEMPLATE لـ template مش approved ← TEMPLATE_NOT_APPROVED.

8.2  اختبارات حقيقية مع Meta (بعد القسم 3.1 و3.2)
     [ ] تفعيل META-WA-INBOUND سجّل الـ subscription، والـ App Dashboard بيبين
         callback URL بـ HTTPS.
     [ ] صاحب المشروع بعت رسالة نصية من رقمه لرقم الاختبار ← اتسجلت مرة واحدة.
     [ ] بعت فويس وصورة ← اتسجلوا بالنوع الصح، ومن غير تنزيل.
     [ ] WA-ECHO-TEST رد على رقم مسموح بيه بس، وهو مفعّل بس.
     [ ] الـ statuses بتاعة رسالة الـ echo وصلت (sent وdelivered وread).
     [ ] template (الـ hello_world الافتراضي بتاع Meta، أو واحد approved من M-03)
         اتبعت لرقم الـ recipient.
     [ ] POST على الـ webhook URL بـ X-Hub-Signature-256 غلط ← مفيش execution
         ومفيش صفوف جديدة.
     [ ] token غلط (credential مؤقت للاختبار) ← TOKEN_INVALID، وصف في
         workflow_errors، وتنبيه لو Telegram موجود.
     [ ] الـ 11 template اتقدموا، والـ sync جاب حالتهم.

8.3  الـ healthcheck بتاع TASK 01 بالإضافات الجديدة، لازم يفضل كله PASS.

==================================================
القسم 9: التوثيق
==================================================

- docs/meta-setup.md: القسم 3 كله، بالعربي، خطوة بخطوة، ومعاه checklist.
- docs/whatsapp-integration.md:
  - Mermaid diagram للفلو: Meta ← tunnel ← trigger ← PROCESS-EVENT ← DB، والإرسال.
  - شرح كل workflow، وinputs/outputs الـ sub-workflows (contract)، وجدول error codes.
  - نافذة الـ 24 ساعة، والـ templates، والقيود (D-1 وD-5).
- docs/runbooks/whatsapp.md:
  - الـ token اتلغى أو انتهى.
  - رابط الـ Quick Tunnel اتغير، أو الجهاز أو Docker عمل restart: إمتى تشغّل
    tunnel-refresh، وإزاي تتأكد إنه نجح، والخطوة اليدوية لو الأوتوماتيك فشل (D-5).
  - بداية يوم الاختبار ونهايته: tunnel-refresh في الأول، وtunnel-down في الآخر.
  - Meta مش بتبعت أحداث.
  - template اترفض.
  - رسائل بتفشل بـ rate limit.
  - خطة نقل رقم المكتب (القسم 3.4).
- حدّث README.md (قسم WhatsApp مختصر)، وdocs/env-variables.md
  (اشرح إن whatsapp.* في app.settings ومش في .env، واشيل WHATSAPP_PHONE_NUMBER_ID
  وWHATSAPP_WABA_ID وGRAPH_API_VERSION من .env.example لو مبقوش بيستخدموا)،
  وn8n/credentials-docs/README.md.

==================================================
القسم 10: شروط القبول
==================================================

[ ] migration 0002 بيشتغل up ثم down ثم up، والصلاحيات صح.
[ ] كل workflows القسم 6 متعملة، ومتصدّرة في n8n/workflows/ من غير أي secret.
[ ] سكريبت import للـ workflows بيرجّعهم في n8n نظيف (جرّبه بعد reset).
[ ] كل اختبارات 8.1 PASS (دليل: output السكريبت).
[ ] كل اختبارات 8.2 PASS، أو مكتوب بالظبط اللي ناقص ومستني صاحب المشروع في إيه.
[ ] الـ healthcheck كله PASS.
[ ] مفيش secret في git (نفس فحص TASK 01).
[ ] tunnel-refresh وtunnel-down موجودين بالنسختين، ودورة الـ refresh اتجربت مرتين
    ورسالة الاختبار وصلت بعد كل مرة (D-5 d).
[ ] echo_test_enabled = false في آخر التاسك.
[ ] رقم المكتب الحالي ما اتلمسش.
[ ] docs/meta-setup.md وdocs/whatsapp-integration.md وdocs/runbooks/whatsapp.md موجودين.

==================================================
القسم 11: التقرير النهائي
==================================================

1. الوضع اللي لقيته (healthcheck، والـ stack، وn8n version).
2. اللي صاحب المشروع عمله عند Meta، واللي لسه ناقص.
3. الملفات اللي اتعملت أو اتعدلت.
4. الـ workflows ودور كل واحد، والـ tables.
5. نتيجة كل اختبار + الدليل.
6. حالة كل template عند Meta.
7. أي اختلاف عن البرومت ده أو عن DECISIONS-v1.0، والسبب.
8. المخاطر والقيود (D-1 وD-5، والـ execution data، والـ templates المرفوضة).
9. اللي TASK 03 محتاجه: شكل wa_inbound_messages، وحالة pending، والـ contract
   بتاع SEND-MESSAGE.

==================================================
ممنوعات نهائية
==================================================

- ممنوع تدخل على حساب Meta أو Facebook، أو تعمل أي إجراء عليه.
- ممنوع تطلب الـ secrets في الشات، أو تكتبها في أي ملف.
- ممنوع تفتح env access في n8n.
- ممنوع تبعت رسالة لأي رقم برا whatsapp.test_recipients.
- ممنوع تنقل رقم المكتب الحالي أو تربطه.
- ممنوع أي منطق خاص بحازم (ردود، تصنيف، أسعار). ده بتاع TASK 04 وما بعده.
- ممنوع تنزّل ميديا أو تبعت أي ملف لأي AI.
- ممنوع تمسح templates عند Meta.
- ممنوع تقول إن حاجة اشتغلت من غير ما تشغلها فعلًا وتعرض الدليل.
```

---

## ملاحظات ليك (مش للـ agent)

- **التاسك ده فيه شغل عليك عند Meta** (القسم 3): تعمل App، وتضيف رقمك كـ recipient، وتعمل System User وToken، وتدخل الـ secrets بنفسك في n8n. الـ agent هيكتبلك دليل خطوة بخطوة ويستنى.
- **الـ Quick Tunnel (قرارك):** الرابط بيتغير مع كل restart للـ tunnel أو لـ Docker أو للجهاز. كل مرة تفتح فيها الجهاز عشان تختبر، شغّل `tunnel-refresh`، وهو هيحدّث الرابط ويسجّله عند Meta. ولما تخلص شغّل `tunnel-down`. أي رسالة تتبعت وقت ما الرابط ميت ممكن تضيع، وده مقبول في الاختبار. في الإنتاج هتحتاج دومين (T-13).
- **Business Verification بياخد أيام.** ابدأ فيه بدري، حتى لو لسه بنجرب على رقم الاختبار.
