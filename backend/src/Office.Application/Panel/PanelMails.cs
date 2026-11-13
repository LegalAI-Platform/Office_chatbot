using System.Net;

namespace Office.Application.Panel;

/// <summary>
/// The panel's e-mails from the no-reply address, in Arabic, right to left. Each carries a plain
/// text part too. Never a password; a code only in the mail that asked for it.
/// </summary>
public static class PanelMails
{
    public const string Office = "مكتب سعد فتحي سعد";

    public static PanelMail VerifyCode(PanelUser u, string code) => Build(u.Email,
        $"رمز تأكيد البريد الإلكتروني: {code}",
        $"أهلًا {u.DisplayName}،",
        ["هذا رمز تأكيد البريد الإلكتروني لحسابك في لوحة المكتب:"],
        code,
        ["الرمز صالح لمدة 10 دقائق، ولمرة واحدة فقط.", "إذا لم تسجّل حسابًا، فتجاهل هذه الرسالة."]);

    public static PanelMail ResetCode(PanelUser u, string code) => Build(u.Email,
        $"رمز تغيير كلمة السر: {code}",
        $"أهلًا {u.DisplayName}،",
        ["طلب أحدهم تغيير كلمة السر لحسابك في لوحة المكتب. هذا هو الرمز:"],
        code,
        ["الرمز صالح لمدة 10 دقائق، ولمرة واحدة فقط.", "إذا لم تطلب ذلك، فتجاهل هذه الرسالة، وستبقى كلمة السر كما هي."]);

    public static PanelMail SomeoneTried(PanelUser u) => Build(u.Email,
        "محاولة تسجيل ببريدك الإلكتروني",
        $"أهلًا {u.DisplayName}،",
        [$"حاول أحدهم إنشاء حساب جديد في لوحة المكتب ببريدك الإلكتروني، وهذا البريد مسجّل بالفعل في حساب قائم (اسم المستخدم: {u.Username}).",
         "إذا نسيت كلمة السر، فاستخدم «نسيت كلمة السر» في صفحة الدخول. وإذا لم تكن أنت، فتجاهل هذه الرسالة."],
        null, []);

    public static PanelMail WaitingForApproval(string managerEmail, PanelUser u) => Build(managerEmail,
        $"حساب جديد في انتظار التفعيل: {u.DisplayName}",
        "أهلًا،",
        [$"أنشأ {u.DisplayName} ({u.Username}، {u.Email}) حسابًا في لوحة المكتب وأكّد بريده الإلكتروني.",
         "لن يتمكن صاحب الحساب من الدخول إلا بعد أن تفعّله وتختار دوره من «الإعدادات ← الحسابات»."],
        null, ["إذا كنت لا تعرفه، فارفضه من المكان نفسه."]);

    public static PanelMail Approved(PanelUser u) => Build(u.Email,
        "تم تفعيل حسابك",
        $"أهلًا {u.DisplayName}،",
        ["فعّل المدير حسابك في لوحة المكتب، ويمكنك الدخول الآن باسم المستخدم وكلمة السر الخاصين بك."],
        null, ["يُستحسن تفعيل التحقق بخطوتين من «حسابي» فور الدخول."]);

    public static PanelMail Rejected(PanelUser u) => Build(u.Email,
        "لم يُقبل طلب الحساب",
        $"أهلًا {u.DisplayName}،",
        ["لم يُقبل طلب حسابك في لوحة المكتب. إذا كان ذلك خطأً، فتواصل مع مدير المكتب."],
        null, []);

    public static PanelMail PasswordChanged(PanelUser u) => Build(u.Email,
        "تم تغيير كلمة السر",
        $"أهلًا {u.DisplayName}،",
        ["تم الآن تغيير كلمة السر الخاصة بحسابك في لوحة المكتب، وتم تسجيل الخروج من جميع الأجهزة التي كانت مسجّلة الدخول."],
        null, ["إذا لم تغيّرها أنت، فتواصل مع مدير المكتب فورًا."]);

    public static PanelMail TwoFactorOn(PanelUser u) => Build(u.Email,
        "تم تفعيل التحقق بخطوتين",
        $"أهلًا {u.DisplayName}،",
        ["تم تفعيل التحقق بخطوتين على حسابك. من الآن، يتطلب الدخول أيضًا الرمز الظاهر في تطبيق Authenticator.",
         "احتفظ برموز الاسترجاع في مكان آمن: فهي التي تتيح لك الدخول إذا فقدت هاتفك."],
        null, ["إذا لم تفعّله أنت، فتواصل مع مدير المكتب فورًا."]);

    public static PanelMail TwoFactorOff(PanelUser u) => Build(u.Email,
        "تم إيقاف التحقق بخطوتين",
        $"أهلًا {u.DisplayName}،",
        ["تم إيقاف التحقق بخطوتين على حسابك، وأصبح الدخول بكلمة السر فقط. وتم تسجيل الخروج من جميع الأجهزة التي كانت مسجّلة الدخول."],
        null, ["إذا لم توقفه أنت أو المدير، فتواصل مع مدير المكتب فورًا."]);

    /// <summary>
    /// TASK 07c: an alert. The kind of task, its due time in Cairo and a link: never the client's
    /// name, number or words (an e-mail leaves the office's machines).
    /// </summary>
    public static PanelMail Alert(string to, AlertToMail a, string panelUrl)
    {
        var (subject, line) = a.Kind switch
        {
            AlertKinds.Overdue => ("مهمة متأخرة", "توجد مهمة تجاوزت موعدها ولم تكتمل بعد."),
            AlertKinds.Urgent => ("طلب عاجل", "يوجد طلب عاجل جديد يحتاج إلى من يتولّاه الآن."),
            AlertKinds.Rating => ("تقييم ضعيف من عميل", "قيّم أحد العملاء الخدمة بـ«يحتاج للتحسين»، ووعدناه بأن يتواصل معه أحد من المكتب."),
            _ => ("عميل يطلب التحدث مع موظف", "طلب أحد العملاء التحدث مع أحد موظفي المكتب."),
        };
        var due = a.DueAt is { } d ? ArabicTime(TimeZoneInfo.ConvertTime(d, Cairo)) : "";
        return Build(to, subject, "أهلًا،",
            [line, $"النوع: {TaskType(a.TaskType ?? "")}", $"المطلوب قبل: {due}"],
            null,
            [$"افتحها من لوحة المكتب: {panelUrl.TrimEnd('/')}/tasks/{a.TaskId}", "اسم العميل ورقمه ورسائله موجودة في اللوحة فقط، وليست في هذه الرسالة."]);
    }

    /// <summary>TASK 13 (v1.18): a job of the system failed. To the managers; what failed and where to look, nothing else.</summary>
    public static PanelMail SystemAlert(string to, string? what, string panelUrl)
    {
        var (subject, line, look) = what switch
        {
            SystemWhat.Backup => ("فشلت النسخة الاحتياطية", "لم تُنشأ النسخة الاحتياطية الليلية.", "شغّل backup مرة أخرى، وإذا فشل فراجع docs/runbooks/backup-restore.md."),
            SystemWhat.Drill => ("فشلت تجربة الاسترجاع", "فشلت تجربة استرجاع النسخة الاحتياطية الشهرية، أي أن النسخة قد لا تصلح عند الحاجة إليها.", "راجع docs/runbooks/backup-restore.md."),
            SystemWhat.LogScan => ("تحتوي السجلات على بيانات لا يجوز تسجيلها", "عثر فحص سجلات النظام على مفتاح أو رقم هاتف كامل.", "راجع docs/runbooks/secrets.md، وغيّر المفتاح إذا كان قد سُجّل."),
            SystemWhat.Turn => ("توقفت معالجة رسالة عميل بعد محاولات عديدة", "حاول النظام الرد على رسالة من أحد العملاء 5 مرات وفشل، فنُحّيت جانبًا حتى لا تتعطل رسائله التالية.", "راجع المحادثة في Chatwoot وردّ عليه بنفسك، وراجع سجل الـ API وقت الرسالة."),
            SystemWhat.Ai => ("الذكاء الاصطناعي الذي يفهم رسائل العملاء متوقف", "خلال الساعة الماضية، لم يتمكن حازم من فهم 3 رسائل أو أكثر لأن الذكاء الاصطناعي لم يستجب: على الأرجح أُوقف مفتاح Anthropic أو انتهت صلاحيته، أو نفد الرصيد. يرد حازم بـ«مش واضحلي» حتى يعود (ولا يتعرّف من دونه إلا على الشكر والشتائم).", "افتح console.anthropic.com وراجع المفتاح والرصيد، وضع مفتاحًا جديدًا في .env على الخادم (docs/runbooks/secrets.md)، ثم شغّل docker compose up -d api."),
            SystemWhat.N8n => ("توجد عمليات فاشلة في n8n", "فشلت بعض الـ workflows في n8n خلال الساعة الماضية.", "افتح n8n وراجع Executions."),
            _ => ("اكتشف فحص النظام مشكلة", "اكتشف الفحص اليومي للنظام (healthcheck) خدمة متوقفة عن العمل.", "شغّل healthcheck وراجع السطر الذي يحتوي على FAIL."),
        };
        return Build(to, subject, "أهلًا،", [line, look], null,
            [$"لوحة المكتب: {panelUrl.TrimEnd('/')}", "لا تحتوي هذه الرسالة على أي بيانات للعملاء."]);
    }

    private static TimeZoneInfo Cairo => global::Office.Domain.Tasks.OfficeHours.Cairo;

    // The API runs with InvariantGlobalization (no culture data), so the Arabic names are written here.
    private static readonly string[] Months =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    /// <summary>e.g. «الخميس 23 أكتوبر، 3:05 م» (Cairo time already).</summary>
    public static string ArabicTime(DateTimeOffset t)
    {
        var hour = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        return $"{Booking.BookingWords.DayName(t.DayOfWeek)} {t.Day} {Months[t.Month - 1]}، {hour}:{t.Minute:00} {(t.Hour < 12 ? "ص" : "م")}";
    }

    /// <summary>The task types as the panel names them.</summary>
    public static string TaskType(string type) => type switch
    {
        "existing_client_message" => "موكّل حالي يستفسر عن قضيته",
        "late_reschedule" => "طلب متأخر لتغيير الموعد",
        "refund" => "استرداد",
        "price_quote" => "تسعير",
        "discount_request" => "طلب خصم",
        "complaint" => "شكوى",
        "human_requested" => "طلب التحدث مع موظف",
        "safety_alert" => "تنبيه أمان",
        "lawyer_direct_message" => "رسالة لقائمة المستشار",
        "external_contact" => "زميل أو صحافة أو مورّد",
        "unclear_message" => "رسالة غير واضحة",
        "service_question" => "استفسار عن خدمة معيّنة",
        "payment_review_due" => "اقترب الموعد ولم يُراجَع التحويل بعد",
        "appointment_unmarked" => "انقضى موعد دون تسجيل نتيجته، ولم يُراجَع التحويل بعد",
        "urgent_request" => "طلب عاجل",
        "service_request" => "طلب خدمة",
        "reschedule_or_cancel" => "تغيير موعد أو إلغاؤه",
        "booking_unavailable" => "حجز: لا توجد مواعيد أو التقويم غير متاح",
        "booking_cancelled" => "عميل ألغى حجزه",
        "booking_message_failed" => "تعذّر وصول رسالة إلى العميل", // v1.44: a booking notice or any reply of Hazem's
        "calendar_conflict" => "حدث في التقويم يتعارض مع حجز",
        "office_cancelled" => "ألغى المكتب حجزًا: موعد آخر أو استرداد",
        _ => type,
    };

    /// <summary>The seal, attached inline by the mailer (Content-ID); the HTML points at it.</summary>
    public const string SealCid = "office-seal";

    private const string Font = "Tahoma,'Segoe UI',Arial,sans-serif";

    /// <summary>
    /// Tables and inline styles (what Gmail, Outlook and phones all keep), right to left on every
    /// cell: Gmail drops dir on html and body.
    /// </summary>
    private static PanelMail Build(string to, string subject, string greeting, string[] lines, string? code, string[] notes)
    {
        var text = string.Join("\n\n", new[] { greeting }.Concat(lines).Concat(code is null ? Array.Empty<string>() : [code]).Concat(notes)
            .Append($"{Office}\nرسالة آلية، والرد عليها لا يصل إلى أحد."));

        static string e(string s) => WebUtility.HtmlEncode(s);
        const string rtl = "direction:rtl;text-align:right;";
        var preheader = code is null ? lines.FirstOrDefault() ?? subject : $"الرمز: {code}. صالح لمدة 10 دقائق.";
        var body = string.Concat(lines.Select(l => $"<p style=\"margin:0 0 12px;{rtl}\">{e(l)}</p>"));
        var codeRow = code is null ? "" : $"""
                <tr><td align="center" style="padding:4px 32px 22px;">
                  <div style="font-family:{Font};font-size:12px;color:#8a7a5c;margin:0 0 8px;">الرمز</div>
                  <div dir="ltr" style="display:inline-block;font-family:Consolas,'Courier New',monospace;font-size:34px;font-weight:700;letter-spacing:10px;color:#1e1a14;background:#f7f3e9;border:1px solid #dcc8a5;border-radius:12px;padding:14px 22px 14px 32px;">{e(code)}</div>
                </td></tr>
            """;
        var noteRow = notes.Length == 0 ? "" : $"""
                <tr><td dir="rtl" align="right" style="padding:0 32px 26px;font-family:{Font};font-size:13px;line-height:1.8;color:#6b6252;{rtl}">
                  {string.Concat(notes.Select(n => $"<p style=\"margin:0 0 6px;{rtl}\">• {e(n)}</p>"))}
                </td></tr>
            """;
        var html = $"""
            <!doctype html>
            <html lang="ar" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{e(subject)}</title></head>
            <body dir="rtl" style="margin:0;padding:0;background:#f4f1ec;">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{e(preheader)}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" dir="rtl" style="background:#f4f1ec;">
              <tr><td align="center" style="padding:28px 12px;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" dir="rtl" style="max-width:560px;background:#ffffff;border:1px solid #e7dfd2;border-radius:16px;overflow:hidden;">
                  <tr><td align="center" style="background:#0f0e0b;padding:26px 24px 22px;">
                    <img src="cid:{SealCid}" width="76" height="76" alt="" style="display:block;border:0;margin:0 auto 12px;">
                    <div style="font-family:{Font};font-size:20px;font-weight:700;color:#e3c896;">{e(Office)}</div>
                    <div style="font-family:{Font};font-size:13px;color:#b9a988;margin-top:4px;">للمحاماة والاستشارات القانونية</div>
                  </td></tr>
                  <tr><td style="height:4px;background-color:#c9a96a;background-image:linear-gradient(90deg,#8b6f47,#e3c896,#8b6f47);font-size:0;line-height:0;">&nbsp;</td></tr>
                  <tr><td dir="rtl" align="right" style="padding:30px 32px 10px;font-family:{Font};font-size:15px;line-height:1.9;color:#1e1a14;{rtl}">
                    <p style="margin:0 0 14px;font-size:17px;font-weight:700;{rtl}">{e(greeting)}</p>
                    {body}
                  </td></tr>
            {codeRow}{noteRow}
                  <tr><td dir="rtl" align="center" style="background:#f7f5f1;border-top:1px solid #ece6db;padding:16px 24px;font-family:{Font};font-size:12px;line-height:1.7;color:#8a8170;text-align:center;">
                    رسالة آلية من لوحة المكتب، والرد عليها لا يصل إلى أحد.<br>{e(Office)} للمحاماة والاستشارات القانونية
                  </td></tr>
                </table>
              </td></tr>
            </table>
            </body></html>
            """;
        return new PanelMail(to, $"{subject} | {Office}", text, html);
    }
}
