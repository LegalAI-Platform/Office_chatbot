# TASK 01 — البنية الأساسية (Infrastructure Bootstrap)

**الاعتماديات:** TASK 00 (`DECISIONS-v1.0.md`)
**البيئة المستهدفة:** Test/Staging فقط (T-01). مفيش إنتاج في التاسك ده.
**التاسك اللي بعده:** TASK 02 — ربط Meta WhatsApp Cloud API

> انسخ كل اللي تحت الخط ده وابعته للـ agent، ومعاه ملف `DECISIONS-v1.0.md`.

---

```text
أنت Senior DevOps / Platform Engineer.

مهمتك تجهيز البنية الأساسية لبيئة الاختبار (Test/Staging) لنظام واتساب لمكتب محاماة
اسمه «مكتب الأستاذ سعد فتحي سعد». المساعد الآلي على واتساب اسمه «حازم».
النظام مبني على n8n كمنسّق للفلو، وMeta WhatsApp Cloud API لواتساب، وPostgreSQL
كقاعدة بيانات البيزنس.

المرجع الوحيد للقرارات هو ملف DECISIONS-v1.0.md المرفق. لو أي حاجة في البرومت ده
اتعارضت معاه، الملف هو اللي يكسب، واذكر التعارض في التقرير.

==================================================
القسم 1: قبل ما تبدأ
==================================================

1. افحص الـ repository الحالي: الملفات، والـ git history، وأي docker-compose أو
   إعدادات موجودة. لو فيه conventions موجودة حافظ عليها. لو الـ repository فاضي،
   قول ده صراحة واستخدم الهيكل اللي في القسم 4.
2. افحص بيئة التشغيل: نظام التشغيل، ونسخة Docker، ونسخة Docker Compose.
   - جهاز التطوير الأساسي Windows 10 عليه Docker Desktop بـ WSL2.
   - البيئة لازم تشتغل كمان على Linux VPS بعد كده من غير تعديل في الـ compose.
3. ما تخترعش حاجة مش موجودة، وما تقولش إنك فحصت ملف ما فحصتهوش.
4. لو فيه قرار مش محسوم في DECISIONS-v1.0 ومهم للتاسك ده، اسأل قبل ما تكمل.

==================================================
القسم 2: النطاق
==================================================

المطلوب في التاسك ده:
- Docker Compose لبيئة الاختبار.
- n8n في Regular mode.
- PostgreSQL 16 فيها قاعدتين: n8n وoffice.
- Redis.
- MinIO كـ Object Storage متوافق مع S3، وفيه 3 buckets.
- Appsmith Community Edition كـ profile اختياري.
- Tunnel لرابط HTTPS مؤقت عشان Meta Webhook، كـ profile اختياري.
- أداة migrations لقاعدة office، ومعاها migration أساسي واحد.
- سكريبتات تشغيل وإيقاف وbackup وrestore وhealthcheck.
- README وتوثيق كل متغيرات البيئة.

خارج النطاق (ممنوع تعمله في التاسك ده):
- أي جدول بيزنس (persons، messages، payments، ...). الجداول بتتعمل في التاسكات
  الخاصة بيها.
- أي n8n workflow.
- ربط Meta أو OpenAI أو Google Calendar أو Telegram.
- بناء صفحات في Appsmith.
- أي إعداد إنتاج: VPS، دومين حقيقي، Cloudflare R2.
- Kubernetes أو أي orchestrator تاني.
- n8n Queue mode أو workers.

==================================================
القسم 3: القرارات اللي التاسك ده بيطبقها
==================================================

T-01  بيئة Test/Staging معزولة، ومفيش أي بيانات عملاء حقيقية.
T-02  Docker Compose.
T-03  n8n Self-hosted في Regular mode.
T-04  PostgreSQL 16، فيها قاعدة n8n وقاعدة office منفصلين.
T-05  Redis موجود من الأول، وهيستخدم بعدين للتجميع المؤقت ومنع التكرار والأقفال
      وتحديد معدل الرسائل.
T-06  MinIO في الاختبار، وbucket منفصل للمستندات الحساسة.
T-11  Appsmith Community Edition Self-hosted.
T-12  التوقيت Africa/Cairo في كل مكان.
T-13  رابط HTTPS مؤقت في الاختبار.
T-15  backup يومي للداتابيز على MinIO، ويتحفظ 30 يوم، مع اختبار استرجاع.
T-09  صور البطاقات والمستندات الحساسة ما تروحش لأي AI provider. (التاسك ده بيجهز
      الفصل على مستوى التخزين بس.)

==================================================
القسم 4: هيكل الـ repository
==================================================

استخدم الهيكل ده لو مفيش هيكل موجود:

/
├── docker-compose.yml
├── .env.example
├── .gitignore
├── .gitattributes
├── README.md
├── docs/
│   ├── decisions/
│   │   └── DECISIONS-v1.0.md        ← انسخ الملف المرفق هنا زي ما هو
│   ├── infrastructure.md
│   └── env-variables.md
├── infra/
│   ├── postgres/
│   │   └── init/
│   │       └── 01-create-databases-and-roles.sh
│   ├── minio/
│   │   └── init-buckets.sh
│   └── redis/
│       └── redis.conf
├── database/
│   └── migrations/
│       └── 0001_base.sql
├── scripts/
│   ├── windows/        ← PowerShell (.ps1)
│   └── linux/          ← Bash (.sh)
└── n8n/
    ├── workflows/      ← فاضي دلوقتي، فيه .gitkeep
    └── credentials-docs/
        └── README.md   ← توثيق الـ credentials اللي هتتعمل بعدين، من غير أي قيم

حدود المجلدات:
- infra/: إعداد الخدمات بس، ومفيش فيه أي business logic.
- database/migrations/: SQL بس، ومفيش فيه أي بيانات حقيقية.
- n8n/workflows/: ملفات workflow JSON. ممنوع يبقى فيها أي secret.
- scripts/: تشغيل وصيانة بس.

==================================================
القسم 5: الخدمات في docker-compose.yml
==================================================

قواعد عامة لكل الخدمات:
- كل image بنسخة محددة (tag ثابت)، ممنوع latest. اختار آخر نسخة stable متاحة
  واكتبها في التقرير.
- restart: unless-stopped.
- healthcheck لكل خدمة.
- كل الـ ports مربوطة على 127.0.0.1 بس، ومفيش ولا خدمة متاحة على الشبكة.
- network داخلية واحدة اسمها office_net.
- named volumes لكل البيانات، ومفيش bind mounts للبيانات.
- كل الإعدادات من .env، ومفيش أي قيمة سرية مكتوبة في الـ compose.
- TZ=Africa/Cairo لكل خدمة بتدعمه.
- depends_on بـ condition: service_healthy.

5.1 postgres
- image: postgres:16 (نسخة 16.x محددة).
- superuser من .env، ومحدش يستخدمه غير سكريبت الـ init.
- سكريبت init (01-create-databases-and-roles.sh) يعمل:
  - قاعدة n8n، ويوزر n8n_user مالكها.
  - قاعدة office، ومعاها 3 يوزرز:
    - office_migrator: مالك الـ schema، وبيستخدمه أداة الـ migrations بس.
    - office_app: SELECT, INSERT, UPDATE, DELETE بس، وممنوع عليه DDL. ده اللي n8n
      هيستخدمه.
    - office_readonly: SELECT بس. ده للتقارير وAppsmith في القراءة.
  - n8n_user ممنوع يوصل لقاعدة office، وoffice_* ممنوع يوصلوا لقاعدة n8n.
  - REVOKE ALL ON DATABASE من PUBLIC على القاعدتين.
  - DEFAULT PRIVILEGES بحيث الجداول اللي office_migrator هيعملها بعدين تاخد صلاحيات
    office_app وoffice_readonly أوتوماتيك.
  - السكريبت idempotent، يعني تشغيله مرتين ما يكسرش حاجة.
  - الباسوردات من environment variables.
- timezone = 'Africa/Cairo' على مستوى الـ server.
- healthcheck بـ pg_isready.
- port 5432 على 127.0.0.1 بس.

5.2 redis
- image: redis:7 (نسخة محددة).
- requirepass من .env.
- appendonly yes.
- maxmemory مناسب لبيئة اختبار (مثلًا 256mb)، وmaxmemory-policy noeviction،
  لأن Redis هيتستخدم للأقفال ومنع التكرار ومش لازم مفاتيح تتمسح لوحدها.
- تعطيل أو إعادة تسمية الأوامر الخطيرة: FLUSHALL, FLUSHDB, CONFIG, DEBUG.
- healthcheck بـ redis-cli ping مع الباسورد.

5.3 minio
- image: minio/minio (release محدد).
- root user وpassword من .env.
- API على 127.0.0.1:9000، وconsole على 127.0.0.1:9001.
- خدمة minio-init (one-shot) بـ minio/mc تعمل:
  - bucket اسمه office-media: صور وفويس وملفات عادية. private.
  - bucket اسمه office-sensitive: صور البطاقات ومستندات العرفي والإيصالات. private،
    وversioning enabled، وobject locking لو متاح من غير تعقيد.
  - bucket اسمه office-backups: نسخ الداتابيز. private، وlifecycle rule تمسح الملفات
    الأقدم من 30 يوم.
  - access key منفصل لـ n8n، وليه policy تسمح بـ office-media وoffice-sensitive بس.
  - access key منفصل للـ backup، وليه policy تسمح بـ office-backups بس.
  - مفيش bucket public، ومفيش anonymous access.
  - السكريبت idempotent.

5.4 n8n
- image: n8nio/n8n (نسخة محددة).
- Regular mode، ومفيش workers.
- قاعدة البيانات PostgreSQL، وممنوع SQLite:
  DB_TYPE=postgresdb وDB_POSTGRESDB_* بيوزر n8n_user وقاعدة n8n.
- N8N_ENCRYPTION_KEY من .env.
  - اكتب في README تحذير واضح: المفتاح ده لو اتغير أو ضاع، كل الـ credentials
    المتخزنة في n8n هتبوظ.
  - اشرح إزاي يتولد: 32 byte random على الأقل، وبأمر واضح على Windows وعلى Linux.
- GENERIC_TIMEZONE=Africa/Cairo وTZ=Africa/Cairo.
- WEBHOOK_URL وN8N_HOST وN8N_PROTOCOL من .env، عشان لما الـ tunnel يشتغل الـ webhook
  URL يبقى HTTPS صح.
- N8N_PROXY_HOPS=1 لما يبقى ورا الـ tunnel.
- N8N_DIAGNOSTICS_ENABLED=false، وتعطيل أي telemetry.
- Execution pruning: EXECUTIONS_DATA_PRUNE=true، وEXECUTIONS_DATA_MAX_AGE مناسب
  لبيئة الاختبار (مثلًا 14 يوم).
- منع Code nodes إنها تقرأ environment variables لو n8n بيدعم ده
  (N8N_BLOCK_ENV_ACCESS_IN_NODE=true). الـ secrets مكانها n8n Credentials بس.
- port 5678 على 127.0.0.1.
- راجع أسماء متغيرات البيئة على توثيق n8n الرسمي للنسخة اللي اخترتها، لأن الأسماء
  ممكن تكون اتغيرت. اكتب في التقرير أي متغير اسمه اختلف.

5.5 appsmith (profile: panel)
- image: appsmith/appsmith-ce (نسخة محددة).
- مش بيشتغل افتراضيًا، بيشتغل بس بـ --profile panel.
- port على 127.0.0.1 (مثلًا 8080).
- ما توصلوش بأي داتابيز في التاسك ده. وثّق بس إنه هيتوصل بعدين بـ office_app
  للكتابة وoffice_readonly للقراءة.

5.6 tunnel (profile: tunnel)
- استخدم cloudflared كـ Quick Tunnel (من غير حساب) أو Named Tunnel لو فيه token في .env.
- الـ tunnel يوجّه لـ n8n:5678 بس. ممنوع يعرض Postgres أو Redis أو MinIO أو Appsmith.
- وثّق في README:
  - إزاي تشغله وتجيب الرابط.
  - إن رابط الـ Quick Tunnel بيتغير مع كل تشغيل، ولازم WEBHOOK_URL يتحدث ويتعمل
    restart لـ n8n، والرابط يتحدث في Meta بعدين (TASK 02).
  - إن الـ Named Tunnel بيدي رابط ثابت، وده الأفضل لما نبدأ نربط Meta.

==================================================
القسم 6: الـ Migrations
==================================================

1. اختار أداة migrations خفيفة بتشغّل SQL عادي، ويفضل dbmate من خلال Docker
   image، عشان ما نحتاجش نثبت حاجة على الجهاز. لو اخترت أداة تانية اذكر السبب.
2. الأداة تتصل بيوزر office_migrator بس.
3. اعمل migration واحد بس: 0001_base.sql، وفيه:
   - CREATE EXTENSION IF NOT EXISTS pgcrypto؛ (عشان gen_random_uuid وتشفير الحقول بعدين)
   - CREATE EXTENSION IF NOT EXISTS citext؛
   - schema اسمها app، وsearch_path لـ office_app وoffice_readonly يبقى app.
   - function عامة اسمها app.set_updated_at() كـ trigger function، عشان الجداول
     اللي جاية تستخدمها.
   - ومعاه down migration.
   - ممنوع أي جدول بيزنس.
4. سكريبت migrate up وmigrate down وmigrate status.

==================================================
القسم 7: ملف .env.example
==================================================

- كل المتغيرات اللي الـ compose محتاجها، وكل قيمة placeholder واضح، زي
  CHANGE_ME_STRONG_PASSWORD.
- كل متغير جنبه comment بيشرح هو إيه، ومين بيستخدمه، وإزاي تولّده.
- اتجمع في أقسام: Postgres، Redis، MinIO، n8n، Appsmith، Tunnel، Backup.
- ضيف متغيرات placeholder للتاسكات الجاية، بس عليها comment «TASK 02» أو «TASK 04»،
  ومن غير قيم:
  WHATSAPP_PHONE_NUMBER_ID، WHATSAPP_WABA_ID، GRAPH_API_VERSION، META_APP_SECRET،
  WHATSAPP_VERIFY_TOKEN، OPENAI_MODEL، OPS_TELEGRAM_CHAT_ID.
  ووضّح إن WHATSAPP_ACCESS_TOKEN وOPENAI_API_KEY وTELEGRAM_BOT_TOKEN هيتحطوا في
  n8n Credentials، مش في .env.
- ممنوع أي secret حقيقي.

وكمان:
- .gitignore يتجاهل .env وأي ملفات backup محلية وأي data folders.
- .gitattributes يفرض LF على .sh و.sql والـ compose، عشان السكريبتات ما تبوظش
  جوه الـ containers لما الملفات تتعمل على Windows.

==================================================
القسم 8: السكريبتات
==================================================

اعمل كل سكريبت مرتين: PowerShell في scripts/windows/ وBash في scripts/linux/.
والمنطق نفسه يفضل جوه containers قد ما تقدر، عشان السكريبتين يبقوا رفاع.

| السكريبت | الوظيفة |
|---|---|
| up | يشغل الخدمات الأساسية: postgres، redis، minio، minio-init، n8n |
| up-panel | يشغل الأساسي + appsmith |
| up-tunnel | يشغل الأساسي + الـ tunnel، ويطبع الرابط |
| down | يوقف الخدمات من غير ما يمسح volumes |
| logs | logs لخدمة محددة |
| status | حالة كل الخدمات والـ healthchecks |
| healthcheck | فحص شامل (تفاصيله في القسم 10) |
| migrate | up / down / status |
| backup | pg_dump لقاعدة office وقاعدة n8n، مضغوط، ويترفع على office-backups، واسم الملف فيه التاريخ والوقت بتوقيت القاهرة |
| restore | يرجّع backup محدد لقاعدة اختبار جديدة (مش فوق القاعدة الأصلية افتراضيًا) |
| reset | يمسح كل الـ volumes. لازم يطلب تأكيد صريح بكتابة كلمة RESET، ويرفض يشتغل لو ENVIRONMENT مش test |
| gen-secrets | يولّد باسوردات ومفاتيح عشوائية ويطبعها، عشان الشخص ينسخها في .env بنفسه. ما يكتبش على .env لوحده |

قواعد السكريبتات:
- ما تطبعش أي باسورد في الـ logs (ماعدا gen-secrets، وده وظيفته).
- تقف عند أول خطأ.
- رسائل واضحة بالإنجليزي.

الـ backup اليومي:
- وثّق إزاي يتعمل جدول يومي:
  - على Windows: Task Scheduler.
  - على Linux: cron.
- ما تضيفش خدمة scheduler جديدة للـ compose عشان الـ backup بس.

==================================================
القسم 9: الأمان
==================================================

- مفيش ولا خدمة متاحة على 0.0.0.0.
- مفيش default passwords. لو .env فيه CHANGE_ME، الخدمات ترفض تشتغل، أو السكريبت up
  يرفض يشغلها.
- n8n هو الخدمة الوحيدة اللي بتتعرض للإنترنت، ومن خلال الـ tunnel بس.
- n8n owner account بيتعمل يدويًا أول مرة. وثّق ده، وما تحطش باسورد افتراضي.
- كل يوزر في الداتابيز بأقل صلاحيات ممكنة (القسم 5.1).
- كل access key في MinIO بأقل صلاحيات ممكنة (القسم 5.3).
- office-sensitive منفصل عشان التاسكات الجاية تمنع أي ملف فيه إنه يروح لأي AI provider.
  وثّق القاعدة دي في docs/infrastructure.md.
- ممنوع أي secret في git، أو في workflow JSON، أو في الـ logs.

==================================================
القسم 10: الـ Healthcheck الشامل
==================================================

سكريبت healthcheck يتأكد من الآتي، ويطبع PASS أو FAIL لكل بند:

1. كل الـ containers حالتها healthy.
2. Postgres:
   - office_app يقدر يتصل بقاعدة office.
   - office_app ما يقدرش يعمل CREATE TABLE.
   - office_readonly ما يقدرش يعمل INSERT.
   - n8n_user ما يقدرش يتصل بقاعدة office.
   - timezone بتاعة الـ server هي Africa/Cairo.
3. n8n:
   - /healthz بيرد 200.
   - n8n شغال على PostgreSQL مش SQLite (اتأكد إن فيه جداول n8n في قاعدة n8n).
4. Redis: PING بالباسورد بيرد PONG، ومن غير باسورد بيترفض.
5. MinIO:
   - الـ 3 buckets موجودين.
   - access key بتاع n8n يقدر يكتب في office-media، وما يقدرش يكتب في office-backups.
   - مفيش bucket عليه anonymous access.
6. Migrations: 0001_base متطبق، وschema app موجودة.

==================================================
القسم 11: التوثيق
==================================================

README.md:
- المتطلبات (Docker Desktop + WSL2 على Windows، أو Docker على Linux).
- أول تشغيل خطوة بخطوة: نسخ .env.example، وgen-secrets، وup، وmigrate، وhealthcheck،
  وإنشاء n8n owner.
- جدول الخدمات: الاسم، والـ port، والرابط المحلي، والغرض.
- إزاي تشغل Appsmith والـ tunnel.
- backup وrestore.
- troubleshooting لأشهر المشاكل: port مستخدم، وWSL2، وline endings، وencryption key.

docs/infrastructure.md:
- diagram بـ Mermaid للخدمات والاتصالات بينها.
- جدول اليوزرز والصلاحيات في Postgres.
- جدول الـ buckets والـ access keys والـ policies.
- قاعدة فصل office-sensitive عن الـ AI.
- إيه اللي هيتغير لما ننقل للإنتاج: R2 بدل MinIO، ودومين حقيقي بدل الـ tunnel،
  وVPS. (توثيق بس، من غير تنفيذ.)

docs/env-variables.md:
- كل متغير: الاسم، والخدمة، وsecret ولا لأ، ومثال placeholder، والتاسك اللي محتاجه.

n8n/credentials-docs/README.md:
- قائمة الـ credentials اللي هتتعمل في n8n في التاسكات الجاية، ونوع كل واحد:
  - Postgres (office_app)
  - S3 (MinIO، key بتاع n8n)
  - Redis
  - WhatsApp / HTTP Header Auth (TASK 02)
  - OpenAI (TASK 04)
  - Google Calendar (TASK 08)
  - Telegram (المراقبة)
- من غير أي قيم.

==================================================
القسم 12: شروط القبول
==================================================

التاسك مايتقفلش غير لما كل البنود دي تتحقق، وتبعت دليل على كل بند (output الأوامر):

[ ] docker compose config بيعدي من غير errors ولا warnings.
[ ] أول تشغيل من الصفر (volumes فاضية) بيشتغل بالخطوات اللي في README بالظبط.
[ ] كل الخدمات الأساسية healthy.
[ ] n8n بيفتح على http://127.0.0.1:5678، وشغال على PostgreSQL.
[ ] Restart لـ n8n ما بيضيعش أي بيانات.
[ ] قاعدة n8n وقاعدة office منفصلين، والصلاحيات زي القسم 5.1 بالظبط.
[ ] الـ 3 buckets موجودين وprivate، والـ policies زي القسم 5.3.
[ ] migrate up ثم down ثم up بيشتغلوا من غير مشاكل.
[ ] backup بيطلع ملف على office-backups، وrestore بيرجّعه لقاعدة جديدة، والبيانات متطابقة.
[ ] healthcheck كله PASS.
[ ] up بيرفض يشتغل لو فيه CHANGE_ME في .env.
[ ] reset بيرفض يشتغل من غير كتابة RESET، وبيرفض لو ENVIRONMENT مش test.
[ ] مفيش أي port على 0.0.0.0 (دليل: docker compose ps أو netstat).
[ ] الـ tunnel بيعرض n8n بس، والـ webhook URL في n8n بيبقى HTTPS.
[ ] Appsmith بيشتغل بـ --profile panel بس.
[ ] مفيش أي secret في git (دليل: git grep على الباسوردات والمفاتيح).
[ ] كل images بنسخ محددة.
[ ] السكريبتات بتشتغل على Windows PowerShell وعلى Bash.

==================================================
القسم 13: شكل التقرير النهائي
==================================================

لما تخلص، ابعت تقرير بالترتيب ده:

1. اللي لقيته في الـ repository قبل ما تبدأ.
2. الملفات اللي اتعملت أو اتعدلت (المسار + سطر عن كل ملف).
3. النسخ المحددة لكل image، والسبب لو اخترت حاجة غير الأحدث.
4. نتيجة كل شرط قبول + الدليل.
5. أي متغير بيئة اسمه طلع مختلف عن البرومت ده، أو أي قرار اتعارض مع DECISIONS-v1.0.
6. أي حاجة ما اتعملتش، والسبب.
7. المخاطر أو القيود المعروفة في البيئة دي.
8. اللي TASK 02 محتاجه من التاسك ده (الـ tunnel URL، والـ credentials، وغيرها).

==================================================
ممنوعات نهائية
==================================================

- ممنوع أي secret حقيقي في أي ملف.
- ممنوع latest tags.
- ممنوع أي خدمة على 0.0.0.0.
- ممنوع SQLite لـ n8n.
- ممنوع جداول بيزنس أو workflows في التاسك ده.
- ممنوع تثبيت أي حاجة على الجهاز غير Docker. كل الأدوات تشتغل من containers.
- ممنوع Kubernetes أو Queue mode أو خدمات ما اتطلبتش.
- ممنوع تقول إن حاجة اشتغلت من غير ما تشغلها فعلًا وتعرض الـ output.
```

---

## ملاحظات ليك (مش للـ agent)

- **ابعت مع البرومت:** ملف `DECISIONS-v1.0.md`، عشان الـ agent يحطه في `docs/decisions/`.
- **محتاج على جهازك:** Docker Desktop شغال على WSL2. ده كل اللي هيتثبت.
- **الـ tunnel:** للاختبار الأول ينفع Quick Tunnel من غير حساب. لما نوصل لربط Meta في TASK 02، الأحسن نعمل Named Tunnel على حساب Cloudflare مجاني، عشان الرابط يفضل ثابت ومانضطرش نحدثه في Meta كل مرة.
- **أول ما التاسك يخلص:** ابعتلي التقرير بتاع القسم 13، وأنا أراجعه على شروط القبول قبل ما نبدأ TASK 02.
