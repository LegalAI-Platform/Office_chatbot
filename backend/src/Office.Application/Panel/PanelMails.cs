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
        $"كود تأكيد الإيميل: {code}",
        $"أهلًا {u.DisplayName}،",
        ["ده كود تأكيد الإيميل لحسابك في لوحة المكتب:"],
        code,
        ["الكود شغال 10 دقايق، ولمرة واحدة.", "لو إنت ما سجلتش حساب، تجاهل الإيميل ده."]);

    public static PanelMail ResetCode(PanelUser u, string code) => Build(u.Email,
        $"كود تغيير كلمة السر: {code}",
        $"أهلًا {u.DisplayName}،",
        ["حد طلب تغيير كلمة السر لحسابك في لوحة المكتب. ده الكود:"],
        code,
        ["الكود شغال 10 دقايق، ولمرة واحدة.", "لو مش إنت اللي طلبت، تجاهل الإيميل ده، وكلمة السر هتفضل زي ما هي."]);

    public static PanelMail SomeoneTried(PanelUser u) => Build(u.Email,
        "محاولة تسجيل بإيميلك",
        $"أهلًا {u.DisplayName}،",
        [$"حد حاول يعمل حساب جديد في لوحة المكتب بإيميلك، وإيميلك عليه حساب أصلًا (اسم المستخدم: {u.Username}).",
         "لو نسيت كلمة السر، استخدم «نسيت كلمة السر» في صفحة الدخول. ولو مش إنت، تجاهل الإيميل ده."],
        null, []);

    public static PanelMail WaitingForApproval(string managerEmail, PanelUser u) => Build(managerEmail,
        $"حساب جديد مستني التفعيل: {u.DisplayName}",
        "أهلًا،",
        [$"{u.DisplayName} ({u.Username}، {u.Email}) عمل حساب في لوحة المكتب وأكّد إيميله.",
         "الحساب مش هيقدر يدخل غير لما تفعّله وتختار دوره من «الإعدادات ← الحسابات»."],
        null, ["لو ما تعرفوش، ارفضه من نفس المكان."]);

    public static PanelMail Approved(PanelUser u) => Build(u.Email,
        "حسابك اتفعّل",
        $"أهلًا {u.DisplayName}،",
        ["المدير فعّل حسابك في لوحة المكتب، وتقدر تدخل دلوقتي باسم المستخدم وكلمة السر بتوعك."],
        null, ["يستحسن تشغّل التحقق بخطوتين من «حسابي» أول ما تدخل."]);

    public static PanelMail Rejected(PanelUser u) => Build(u.Email,
        "طلب الحساب ما اتقبلش",
        $"أهلًا {u.DisplayName}،",
        ["طلب الحساب بتاعك في لوحة المكتب ما اتقبلش. لو ده غلط، كلّم مدير المكتب."],
        null, []);

    public static PanelMail PasswordChanged(PanelUser u) => Build(u.Email,
        "كلمة السر اتغيرت",
        $"أهلًا {u.DisplayName}،",
        ["كلمة السر بتاعة حسابك في لوحة المكتب اتغيرت دلوقتي، وكل الأجهزة اللي كانت داخلة اتقفلت."],
        null, ["لو مش إنت اللي غيرتها، كلّم مدير المكتب حالًا."]);

    public static PanelMail TwoFactorOn(PanelUser u) => Build(u.Email,
        "التحقق بخطوتين اتشغّل",
        $"أهلًا {u.DisplayName}،",
        ["التحقق بخطوتين اتشغّل على حسابك. من دلوقتي الدخول محتاج كمان الكود اللي في تطبيق الـ Authenticator.",
         "احتفظ بأكواد الاسترجاع في مكان آمن: هي اللي هتدخّلك لو الموبايل ضاع."],
        null, ["لو مش إنت اللي شغلته، كلّم مدير المكتب حالًا."]);

    public static PanelMail TwoFactorOff(PanelUser u) => Build(u.Email,
        "التحقق بخطوتين اتقفل",
        $"أهلًا {u.DisplayName}،",
        ["التحقق بخطوتين اتقفل على حسابك، والدخول بقى بكلمة السر بس. وكل الأجهزة اللي كانت داخلة اتقفلت."],
        null, ["لو مش إنت ولا المدير اللي قفله، كلّم مدير المكتب حالًا."]);

    /// <summary>
    /// TASK 07c: an alert. The kind of task, its due time in Cairo and a link: never the client's
    /// name, number or words (an e-mail leaves the office's machines).
    /// </summary>
    public static PanelMail Alert(string to, AlertToMail a, string panelUrl)
    {
        var (subject, line) = a.Kind switch
        {
            AlertKinds.Overdue => ("مهمة اتأخرت", "فيه مهمة عدّى ميعادها ولسه ما خلصتش."),
            AlertKinds.Urgent => ("طلب عاجل", "فيه طلب عاجل جديد محتاج حد يمسكه دلوقتي."),
            _ => ("عميل عايز يكلم حد", "فيه عميل طلب يكلم حد من المكتب."),
        };
        var due = ArabicTime(TimeZoneInfo.ConvertTime(a.DueAt, Cairo));
        return Build(to, subject, "أهلًا،",
            [line, $"النوع: {TaskType(a.TaskType)}", $"المطلوب قبل: {due}"],
            null,
            [$"افتحها من لوحة المكتب: {panelUrl.TrimEnd('/')}/tasks/{a.TaskId}", "اسم العميل ورقمه وكلامه في اللوحة بس، مش في الإيميل."]);
    }

    private static TimeZoneInfo Cairo => global::Office.Domain.Tasks.OfficeHours.Cairo;

    // The API runs with InvariantGlobalization (no culture data), so the Arabic names are written here.
    private static readonly string[] Days = ["الأحد", "الاتنين", "التلات", "الأربع", "الخميس", "الجمعة", "السبت"];
    private static readonly string[] Months =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    /// <summary>e.g. «الخميس 23 أكتوبر، 3:05 م» (Cairo time already).</summary>
    public static string ArabicTime(DateTimeOffset t)
    {
        var hour = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        return $"{Days[(int)t.DayOfWeek]} {t.Day} {Months[t.Month - 1]}، {hour}:{t.Minute:00} {(t.Hour < 12 ? "ص" : "م")}";
    }

    /// <summary>The task types as the panel names them.</summary>
    public static string TaskType(string type) => type switch
    {
        "existing_client_message" => "موكل حالي بيسأل عن موضوعه",
        "late_reschedule" => "تغيير ميعاد متأخر",
        "refund" => "استرداد",
        "price_quote" => "تسعير",
        "discount_request" => "طلب خصم",
        "complaint" => "شكوى",
        "human_requested" => "طلب يكلم موظف",
        "safety_alert" => "تنبيه أمان",
        "lawyer_direct_message" => "رسالة لقايمة الأستاذ",
        "external_contact" => "زميل أو صحافة أو مورّد",
        "unclear_message" => "رسالة مش واضحة",
        "urgent_request" => "طلب عاجل",
        "service_request" => "طلب خدمة",
        "reschedule_or_cancel" => "تغيير أو إلغاء ميعاد",
        "booking_unavailable" => "حجز: مفيش مواعيد أو الكالندر مش متاح",
        "booking_cancelled" => "عميل لغى حجزه",
        "booking_message_failed" => "رسالة حجز ما وصلتش للعميل",
        "calendar_conflict" => "حدث في الكالندر فوق حجز",
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
            .Append($"{Office}\nإيميل آلي، الرد عليه مش بيوصل لحد."));

        static string e(string s) => WebUtility.HtmlEncode(s);
        const string rtl = "direction:rtl;text-align:right;";
        var preheader = code is null ? lines.FirstOrDefault() ?? subject : $"الكود: {code}. شغال 10 دقايق.";
        var body = string.Concat(lines.Select(l => $"<p style=\"margin:0 0 12px;{rtl}\">{e(l)}</p>"));
        var codeRow = code is null ? "" : $"""
                <tr><td align="center" style="padding:4px 32px 22px;">
                  <div style="font-family:{Font};font-size:12px;color:#8a7a5c;margin:0 0 8px;">الكود</div>
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
                    إيميل آلي من لوحة المكتب، الرد عليه مش بيوصل لحد.<br>{e(Office)} للمحاماة والاستشارات القانونية
                  </td></tr>
                </table>
              </td></tr>
            </table>
            </body></html>
            """;
        return new PanelMail(to, $"{subject} | {Office}", text, html);
    }
}
