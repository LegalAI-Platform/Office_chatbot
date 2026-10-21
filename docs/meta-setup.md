# دليل تجهيز Meta (TASK 02)

الدليل ده بتعمله **إنت بنفسك** على حساب Meta بتاعك. الـ agent ممنوع يدخل على حسابك.

**قاعدتين ثابتين:**
- **القيم اللي مش secret** (الأرقام والـ IDs) ابعتها في الشات، وهي متعلّمة في الدليل بـ 📤.
- **الـ secrets** (App Secret والـ Token) **ما تتبعتش في الشات خالص**. بتدخلها بنفسك في n8n، وهي متعلّمة بـ 🔒.

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
| 🔒 **App Secret** (دوس **Show**) | **ما تبعتهوش.** هتحطه في n8n في الجزء 3 |

> ✅ **علامة النجاح:** في **API Setup** دوس **Send message**. لو رسالة `hello_world` وصلتلك على واتساب، الجزء ده تمام.

---

## الجزء 2: Token دائم (System User)، حوالي 10 دقايق

الـ token اللي بيظهر في **API Setup** مؤقت وبينتهي بعد 24 ساعة. محتاجين token دائم.

### 2.1 اعمل System User
1. افتح https://business.facebook.com/settings (Business Settings).
2. **Users ← System users ← Add**.
3. الاسم: `office-n8n`. الدور: **Admin**.

### 2.2 اديله صلاحيات
1. اختار `office-n8n` ← **Assign assets** (أو **Add assets**).
2. **Apps** ← `Office WhatsApp Test` ← **Full control** (أو **Manage app**).
3. **WhatsApp accounts** ← الحساب التجريبي ← **Full control**.
4. **Save**.

### 2.3 اعمل الـ Token
1. وانت على `office-n8n` دوس **Generate new token**.
2. الـ App: `Office WhatsApp Test`.
3. **Token expiration: Never**.
4. الصلاحيات (Permissions):
   - `whatsapp_business_messaging`
   - `whatsapp_business_management`
   - `business_management`
5. **Generate**. 🔒 انسخ الـ token **دلوقتي**، لأنه مش هيظهر تاني. **ما تبعتهوش في الشات.**

---

## الجزء 3: إدخال الـ secrets في n8n (🔒 إنت بس)

افتح http://127.0.0.1:5678 ← القائمة الشمال ← **Credentials** (أو **Overview ← Credentials**) ← **Create credential**.

### 3.1 `meta-whatsapp-trigger`
- النوع: ابحث عن **WhatsApp OAuth API** (ده الـ credential بتاع WhatsApp Trigger).
- **Client ID** = 📤 App ID
- **Client Secret** = 🔒 App Secret
- **اسم الـ credential لازم يبقى بالظبط:** `meta-whatsapp-trigger`
- **Save**.

### 3.2 `meta-whatsapp-token`
- النوع: **Header Auth**.
- **Name** = `Authorization`
- **Value** = `Bearer ` وبعدها الـ token. لازم كلمة `Bearer` ومسافة واحدة قبل الـ token.
- **اسم الـ credential لازم يبقى بالظبط:** `meta-whatsapp-token`
- **Save**.

### 3.3 `meta-app-token`
الـ credential ده بيستخدمه `tunnel-refresh` عشان يمسح الاشتراك القديم عند Meta لما رابط الـ tunnel يتغير.
- النوع: **Query Auth**.
- **Name** = `access_token`
- **Value** = 📤 App ID، وبعده علامة `|`، وبعدها 🔒 App Secret، من غير مسافات.
  الشكل: `1234567890123456|abcdef0123456789...`
- **اسم الـ credential لازم يبقى بالظبط:** `meta-app-token`
- **Save**.

> لو n8n عرض **Test** أو **Retry** وطلع خطأ، ما تقلقش. الـ agent هيتأكد منهم بعدين.

### 3.4 `openai-transcription` (تفريغ الفويس، TASK 03) 🔒
من غير الـ credential ده، الفويس بيتحفظ عادي، بس بيوصل لـ .NET **من غير نص**.
1. من https://platform.openai.com ← **API keys** ← **Create new secret key**. يُفضّل مفتاح
   منفصل للفويس بس (T-08)، عشان تعرف تكلفته وتقدر تقفله لوحده. 🔒 **ما تبعتهوش في الشات.**
2. في n8n اعمل credential من نوع **Header Auth**:
   - **Name** = `Authorization`
   - **Value** = `Bearer ` وبعدها المفتاح.
   - **اسم الـ credential لازم يبقى بالظبط:** `openai-transcription`
3. **Save**، وبعدها شغّل `n8n-import` مرة، عشان الـ workflows تربط نفسها بيه.
4. الموديل في `ai.transcription_model` (الافتراضي `whisper-1`). ثبّته بعد ما تجرّب على
   فويسات حقيقية بالعامية المصرية، وقول في الشات لو عايز تغيّره.

> الـ agent والاختبارات **عمرهم ما بيقروا أو بيغيّروا** الـ credential ده. الاختبار بيعمل
> credential مؤقت بس لو مش موجود، وبيمسحه في الآخر.

### 3.5 (اختياري) Telegram للتنبيهات
لو عاوز تنبيه على Telegram لما حاجة تفشل:
1. على Telegram كلّم **@BotFather** ← `/newbot` ← انسخ الـ token 🔒.
2. ابعت أي رسالة للبوت الجديد من حسابك.
3. في n8n اعمل credential من نوع **Telegram API**، وحط فيه الـ token، وسمّيه `ops-telegram`.
4. 📤 قول في الشات إنك عملته، والـ agent هيجيب الـ chat id.

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
- [ ] 3.1: credential `meta-whatsapp-trigger` اتعمل في n8n.
- [ ] 3.2: credential `meta-whatsapp-token` اتعمل في n8n.
- [ ] 3.3: credential `meta-app-token` اتعمل في n8n.
- [ ] 3.4: credential `openai-transcription` اتعمل في n8n (للفويس، TASK 03).
- [ ] 3.5 (اختياري): `ops-telegram` اتعمل.
- [ ] 4.1: Business Verification بدأ. الحالة: ________
- [ ] 5: ⛔ لسه. رقم المكتب ما اتلمسش.
