# دليل تجهيز Meta (TASK 02)

الدليل ده بتعمله **إنت بنفسك** على حساب Meta بتاعك. الـ agent ممنوع يدخل على حسابك.

**قاعدتين ثابتين:**
- **القيم اللي مش secret** (الأرقام والـ IDs) ابعتها في الشات، وهي متعلّمة في الدليل بـ 📤.
- **الـ secrets** (App Secret والـ Token) **ما تتبعتش في الشات خالص**. بتكتبها بنفسك في ملف `.env` على جهازك (الجزء 3)، وهي متعلّمة بـ 🔒.

> **اتغير إيه عن النسخة اللي فاتت؟** مفيش n8n خلاص (DECISIONS v1.1). الـ secrets بتروح في `.env` بدل n8n Credentials، والـ backend هو اللي بيسجّل الـ webhook عند Meta أوتوماتيك. **الجزء 1 و2 زي ما هم بالظبط**، فلو كنت خلّصتهم كمّل من الجزء 3.

> أسماء الأزرار في Meta بتتغير من وقت للتاني. لو لقيت اسم مختلف شوية، دوّر على أقرب اسم ليه.

---

## الجزء 1: App ورقم اختبار (ابدأ بيه، بياخد حوالي 15 دقيقة)

هنشتغل الأول على **رقم الاختبار المجاني** اللي Meta بتديه، مش رقم المكتب. السبب إن حازم لسه مش مبني، وأي رد غلط على رقم المكتب هيوصل لعملاء حقيقيين.

### 1.1 اعمل الـ App
1. افتح https://developers.facebook.com وادخل بحسابك.
2. **My Apps** ← **Create App**.
3. نوع الـ App: **Business**. ولو ظهرلك سؤال عن الـ use case، اختار **Connect with customers through WhatsApp**.
4. الاسم: `Office WhatsApp Test`.
5. اختار **Business Account** بتاع المكتب، اللي إنت عامله قبل كده.
6. **Create App**.

### 1.2 ضيف WhatsApp
1. في صفحة الـ App ← **Add product** ← **WhatsApp** ← **Set up**.
2. Meta هتعمل تلقائيًا **WhatsApp Business Account تجريبي** ومعاه **Test phone number**.
3. روح **WhatsApp ← API Setup** (أو **Getting started**).

### 1.3 ضيف رقمك كمستقبل
رقم الاختبار ما بيبعتش غير لأرقام إنت بتضيفها، لحد 5 أرقام.
1. في **API Setup** ← خانة **To** ← **Manage phone number list**.
2. ضيف رقم موبايلك اللي عليه واتساب، بالصيغة الدولية (`+20...`).
3. هيوصلك كود على واتساب. اكتبه عشان تأكد الرقم.

### 1.4 سجّل القيم دي (📤 ابعتها في الشات)
من **API Setup**:

| القيمة | فين | مثال للشكل |
|---|---|---|
| 📤 **Phone number ID** | تحت رقم الاختبار | `1234567890123456` |
| 📤 **WhatsApp Business Account ID** | جنب الـ Phone number ID | `1234567890123456` |
| 📤 **رقم الاختبار نفسه** | خانة **From** | `+1 555 ...` |
| 📤 **رقمك اللي ضفته** | خانة **To** | `+2010...` |

ومن **App settings ← Basic**:

| القيمة | مثال للشكل |
|---|---|
| 📤 **App ID** | `1234567890123456` |
| 🔒 **App Secret** (دوس **Show**) | **ما تبعتهوش.** هتحطه في `.env` في الجزء 3 |

> ✅ **علامة النجاح:** في **API Setup** دوس **Send message**. لو رسالة `hello_world` وصلتلك على واتساب، الجزء ده تمام.

---

## الجزء 2: Token دائم (System User)، حوالي 10 دقايق

الـ token اللي بيظهر في **API Setup** مؤقت وبينتهي بعد 24 ساعة. محتاجين token دائم.

### 2.1 اعمل System User
1. افتح https://business.facebook.com/settings (Business Settings).
2. **Users ← System users ← Add**.
3. الاسم: `office-backend`. الدور: **Admin**.
   (لو كنت عملته قبل كده باسم `office-n8n`، سيبه زي ما هو، مفيش مشكلة.)

### 2.2 اديله صلاحيات
1. اختار الـ System User ← **Assign assets** (أو **Add assets**).
2. **Apps** ← `Office WhatsApp Test` ← **Full control** (أو **Manage app**).
3. **WhatsApp accounts** ← الحساب التجريبي ← **Full control**.
4. **Save**.

### 2.3 اعمل الـ Token
1. وانت على الـ System User دوس **Generate new token**.
2. الـ App: `Office WhatsApp Test`.
3. **Token expiration: Never**.
4. الصلاحيات (Permissions):
   - `whatsapp_business_messaging`
   - `whatsapp_business_management`
   - `business_management`
5. **Generate**. 🔒 انسخ الـ token **دلوقتي**، لأنه مش هيظهر تاني. **ما تبعتهوش في الشات.**

---

## الجزء 3: كتابة القيم في `.env` (🔒 إنت بس)

`.env` موجود في أول فولدر المشروع (`WHAT\.env`)، وعليه `.gitignore` فمش بيدخل git أبدًا.

> ✅ **الـ backend جاهز.** الأسطر دي موجودة فعلًا في آخر `.env` وقيمها فاضية، فاملاها بس.

### 3.1 افتح الملف
- من PowerShell، وانت في فولدر المشروع:
  ```powershell
  notepad .env
  ```
- **قواعد الكتابة:** `الاسم=القيمة` في سطر واحد، من غير مسافات حوالين `=`، ومن غير علامات تنصيص.

### 3.2 الـ Verify Token
ده كلمة سر بتتأكد بيها Meta إن الـ webhook بتاعنا. **اتولّد خلاص** واتكتب في `.env` في سطر `WhatsApp__VerifyToken=`، فسيبه زي ما هو وما تغيّروش.

### 3.3 القيم المطلوبة
```env
# --- WhatsApp (Meta Cloud API) ---
WhatsApp__Enabled=true
WhatsApp__AppId=📤 App ID
WhatsApp__WabaId=📤 WhatsApp Business Account ID
WhatsApp__PhoneNumberId=📤 Phone number ID
WhatsApp__TestRecipients=📤 رقمك من غير + ومن غير مسافات، زي 2010xxxxxxxx
WhatsApp__AppSecret=🔒 App Secret
WhatsApp__AccessToken=🔒 الـ token الدائم من الجزء 2.3 (من غير كلمة Bearer)
WhatsApp__VerifyToken=(موجود خلاص، ما تغيّروش)
```
- لو ضفت أكتر من رقم كمستقبل: `WhatsApp__TestRecipients=2010xxxxxxxx,2011xxxxxxxx`.
- احفظ الملف واقفله.

> **ما تحتاجش تدخل على Meta وتكتب الـ Callback URL بإيدك.** أمر `tunnel-refresh` بيسجّل الـ webhook عند Meta أوتوماتيك بالقيم دي، وبيحدّثه كل ما رابط الـ tunnel يتغير.

### 3.4 (اختياري) Telegram للتنبيهات
لو عاوز تنبيه على Telegram لما حاجة تفشل:
1. على Telegram كلّم **@BotFather** ← `/newbot` ← انسخ الـ token 🔒.
2. ابعت أي رسالة للبوت الجديد من حسابك.
3. ضيف في `.env`:
   ```env
   Telegram__BotToken=🔒 token البوت
   ```
4. 📤 قول في الشات إنك عملته، وأنا هجيب الـ chat id وأضيفه.

---

## الجزء 4: ابدأه النهارده (بياخد أيام عند Meta)

### 4.1 Business Verification
لازم يخلص قبل ما نستخدم رقم المكتب الحقيقي، وقبل ما حدود الإرسال تتفتح.
1. **Business Settings ← Security Center** (أو **Business info**) ← **Start verification**.
2. هيتطلب منك:
   - الاسم القانوني للمكتب، والعنوان، والتليفون، والموقع (لو موجود).
   - مستند رسمي باسم المكتب، زي السجل الضريبي أو السجل التجاري أو كارنيه النقابة. Meta بتحدد المقبول حسب البلد.
   - طريقة تأكيد: إيميل على دومين، أو تليفون، أو مستند.
3. المراجعة بتاخد من يوم لكام يوم، وأحيانًا Meta بتطلب مستندات زيادة.

### 4.2 Display Name
- الاسم المطلوب: **مكتب الأستاذ سعد فتحي سعد**.
- لازم يبقى قريب من الاسم الموثّق في 4.1، وMeta بتراجعه.
- بيتعمل لما نضيف الرقم الحقيقي (الجزء 5). رقم الاختبار مش محتاجه.

📤 قولّي في الشات: بدأت الـ verification ولا لأ، وحالته إيه.

---

## الجزء 5: رقم المكتب الحالي (⛔ ممنوع تنفذه دلوقتي)

ده للعلم بس. التنفيذ بعد ما حازم يتبني ويتجرب، وبقرار منك.

**قبل أي حاجة:** خد backup لمحادثات واتساب المكتب، من **Settings ← Chats ← Chat backup**.

فيه طريقتين:

| الطريقة | الفكرة | المميزات | العيوب |
|---|---|---|---|
| **Coexistence** | الرقم يفضل شغال على تطبيق WhatsApp Business على الموبايل، ويتربط بالـ API في نفس الوقت | الموظف يقدر يرد من الموبايل، وجزء من تاريخ المحادثات بيتنقل | لازم يكون متاح لرقمك ودولتك، وليه قيود على بعض المميزات. هنتأكد من الشروط وقتها |
| **Migration** | الرقم يتفك من التطبيق ويتنقل للـ API بس | أبسط وأوضح | المحادثات القديمة **مش هتتنقل**، والموبايل مش هيستقبل على الرقم ده تاني |

خطة النقل خطوة بخطوة هتبقى في `docs/runbooks/whatsapp.md`.

---

## الـ Checklist

ابعتلي الحالة في الشات، بالقيم 📤 بس.

- [ ] 1.1–1.2: الـ App اتعمل، وWhatsApp اتضاف.
- [ ] 1.3: رقمي متضاف كمستقبل ومتأكد.
- [ ] 1.4 📤: App ID، وWABA ID، وPhone number ID، ورقم الاختبار، ورقمي.
- [ ] 1.4 ✅: رسالة `hello_world` وصلتني.
- [ ] 2: System User اتعمل، والـ token الدائم اتعمل.
- [ ] 3.3: كل أسطر `WhatsApp__*` اتكتبت في `.env`، ومنها `WhatsApp__Enabled=true`.
- [ ] بعدها شغّل `up` ثم `tunnel-refresh`، وقولّي، وأنا أكمّل الاختبارات الحقيقية مع Meta.
- [ ] 3.4 (اختياري): `Telegram__BotToken` اتكتب في `.env`.
- [ ] 4.1: Business Verification بدأ. الحالة: ________
- [ ] 5: ⛔ لسه. رقم المكتب ما اتلمسش.
