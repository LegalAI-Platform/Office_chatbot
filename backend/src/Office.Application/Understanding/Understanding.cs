using System.Text;
using System.Text.RegularExpressions;
using Office.Application.Responders;
using Office.Application.Turns;

namespace Office.Application.Understanding;

// T-07: the AI only classifies the turn into a closed list and extracts three fields. It never
// writes a word the client sees. T-09: it sees text only.

public enum Intent
{
    Greeting,
    ProvideName,
    ConsultationRequest,
    ServiceByName,
    Urgent,
    PriceQuestion,
    LegalQuestion,
    ExistingMatter,
    HumanRequest,
    RescheduleOrCancel,
    ThanksOrClosing,
    Other,

    /// <summary>B-25: the client tells their problem without asking anything.</summary>
    DescribeMatter,

    /// <summary>S-01: insults, threats or spam.</summary>
    Abusive,

    /// <summary>TASK 06 (B-20, 1.17): a question about the office itself; <see cref="InfoTopic"/> says which.</summary>
    OfficeInfo,

    /// <summary>
    /// v1.22 (B-20): clear, but nothing a law office does (prayer times, code, chit-chat, a request to change or
    /// reveal the assistant's instructions): the polite off-topic text, never an office task.
    /// </summary>
    OffTopic,

    /// <summary>v1.39 (TASK 16): «خدمات»، «عندكم إيه؟»: the services list, as the welcome's button.</summary>
    ShowServices,

    /// <summary>v1.39: «عاوز احكي مشكلتي»، «عندي مشكلة» without the problem itself: as the «أحكي مشكلتي» button.</summary>
    TellProblem,

    /// <summary>v1.39: «فاكرني؟»، «ميعادي امتى؟»، «دفعت ولا لأ؟»: what the client has with the office (his situation).</summary>
    MyStatus,
}

/// <summary>v1.39 (TASK 16): the gender of the name the client typed (never guessed from anything else).</summary>
public enum NameGender
{
    Male,
    Female,
    Unknown,
}

/// <summary>
/// v1.24 (B-20): the subject of the turn, decided by the AI before the intent. Outside means clearly neither law nor
/// this office (another profession, a shop, a trip…): the code answers off_topic whatever intent came with it.
/// </summary>
public enum Scope
{
    Office,
    Outside,
}

/// <summary>B-20 (1.17): what the client asks about the office (with intent office_info).</summary>
public enum InfoTopic
{
    Address,
    Hours,
    HowToBook,
    HowToPay,
    Refund,
    Mode,
    Prices,
    FeePolicy,
    PracticeAreas,

    /// <summary>v1.22: who or what Hazem is (his name, a bot or not, whether he remembers the client).</summary>
    Assistant,

    /// <summary>v1.22: who the lawyer is, his experience (the approved short text from the owner's words).</summary>
    Lawyer,

    /// <summary>v1.22: whether the office does one specific thing (a licence, a registration…): the office answers.</summary>
    ServiceCheck,

    /// <summary>
    /// v1.46: a question about how one of the office's services goes (is it private, how long, which papers), not a wish
    /// for it: the office answers it, and the service's own path goes on.
    /// </summary>
    ServiceDetail,

    /// <summary>
    /// v1.46 (the owner: «المكتب مش بيفشي اسرار العملاء»): whether what he tells the office stays private, about any service
    /// or none. Hazem answers it himself: yes.
    /// </summary>
    Privacy,
}

/// <summary>B-25: the topic of the request (for the office only, never told to the client).</summary>
public enum MatterType
{
    PersonalStatus,
    Inheritance,
    Rent,
    RealEstate,
    Criminal,
    ChequesTrustReceipts,
    Labor,
    Commercial,
    CivilCompensation,
    Urfi,
    Unclear,
}

/// <summary>B-25: details for personal status and Urfi only.</summary>
public enum MatterSubtype
{
    Divorce,
    Khula,
    Alimony,
    Custody,
    Visitation,
    Marriage,
    UrfMarriage,
    UrfDivorce,
    UrfProof,
}

/// <summary>B-14.4: who is writing, from their own words.</summary>
public enum SenderRole
{
    Client,
    LawyerColleague,
    Press,
    Vendor,
}

/// <summary>Service codes: B-23 rows, plus the three Urfi paths of B-11.</summary>
public enum Service
{
    ConsultationPhoneVideo,
    ConsultationOffice,
    ConsultationUrgent,
    DocumentReview,
    ContractDrafting,
    FormalNotice,
    UrfMarriage,
    UrfDivorce,
    UrfProof,
    UrfUnclear,
    WrittenOpinion,
    CaseRepresentation,
}

public enum ConsultationMode
{
    PhoneVideo,
    Office,
    Unknown,
}

/// <summary>What the model answered, before any check (strings, as they came).</summary>
public sealed record RawClassification(
    string? Intent,
    string? Service,
    string? ConsultationMode,
    string? FullName,
    double? Confidence,
    string? MatterType = null,
    string? MatterSubtype = null,
    string? SenderRole = null,
    string? InfoTopic = null,
    string? Scope = null,
    string? Courtesy = null,
    string? NameGender = null);

/// <summary>One call to the classifier: the answer, or why there is none, plus what it cost.</summary>
public sealed record ClassifierOutcome(
    RawClassification? Answer,
    string? FailureReason,
    string Model,
    string PromptVersion,
    int LatencyMs,
    int? InputTokens = null,
    int? OutputTokens = null)
{
    public const string NoKey = "no_key";
    public const string Timeout = "timeout";
    public const string Error = "error";
    public const string InvalidOutput = "invalid_output";
}

public interface IIntentClassifier
{
    /// <summary>Classifies text that already went through <see cref="AiInput"/>. Never throws for an AI failure.</summary>
    Task<ClassifierOutcome> ClassifyAsync(string text, HazemSettings settings, CancellationToken ct);
}

/// <summary>The checked result. A fallback is always intent other.</summary>
public sealed record Classification(
    Intent Intent,
    Service? Service,
    ConsultationMode Mode,
    string? FullName,
    double Confidence,
    string? FallbackReason,
    ClassifierOutcome? Call,
    MatterType? Matter = null,
    MatterSubtype? Subtype = null,
    SenderRole Role = SenderRole.Client,
    InfoTopic? Topic = null,
    Scope Scope = Scope.Office,
    string? BackstopFor = null,
    CourtesyKind Courtesy = CourtesyKind.None,
    NameGender Gender = NameGender.Unknown)
{
    /// <summary>v1.39: the person's stored value for <see cref="Gender"/> (null when not clear).</summary>
    public string? GenderCode => FullName is null || Gender == NameGender.Unknown ? null : Code(Gender);

    /// <summary>
    /// 0032: the AI failed for this reason and <see cref="Backstop"/> read the message by code instead (the intent is
    /// what it read; not a fallback). The classification log says <c>backstop:&lt;reason&gt;</c>.
    /// </summary>
    public string? LoggedReason => FallbackReason ?? (BackstopFor is { } b ? Backstop.Prefix + b : null);

    public const string UnknownValue = "unknown_value";
    public const string LowConfidence = "low_confidence";
    public const string MinConfidenceKey = "ai.min_confidence";
    public const double DefaultMinConfidence = 0.6;

    public bool IsFallback => FallbackReason is not null;

    public static Classification Fallback(string reason, ClassifierOutcome? call) =>
        new(Intent.Other, null, ConsultationMode.Unknown, null, 0, reason, call);

    /// <summary>
    /// Any value outside the closed lists, a failed call or a confidence under ai.min_confidence
    /// → intent other (the "unclear" answer). No message ever stays unanswered because of the AI.
    /// </summary>
    public static Classification From(ClassifierOutcome call, HazemSettings settings)
    {
        if (call.Answer is not { } a)
        {
            return Fallback(call.FailureReason ?? ClassifierOutcome.Error, call);
        }

        if (!TryEnum<Intent>(a.Intent, out var intent)
            || (a.Service is not null && !TryEnum<Service>(a.Service, out _))
            || (a.ConsultationMode is not null && !TryEnum<ConsultationMode>(a.ConsultationMode, out _))
            || a.Confidence is not (>= 0 and <= 1))
        {
            return Fallback(UnknownValue, call);
        }

        var min = double.TryParse(settings.Get(MinConfidenceKey), System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : DefaultMinConfidence;
        if (a.Confidence < min)
        {
            return Fallback(LowConfidence, call);
        }

        Service? service = a.Service is null ? null : Parse<Service>(a.Service);
        var mode = a.ConsultationMode is null ? ConsultationMode.Unknown : Parse<ConsultationMode>(a.ConsultationMode);
        // The secondary fields (B-25, B-14.4) never fail the turn: a value outside their lists is dropped.
        MatterType? matter = TryEnum<MatterType>(a.MatterType, out var mt) ? mt : null;
        MatterSubtype? subtype = TryEnum<MatterSubtype>(a.MatterSubtype, out var ms) ? ms : null;
        var role = TryEnum<SenderRole>(a.SenderRole, out var sr) ? sr : SenderRole.Client;
        InfoTopic? topic = TryEnum<InfoTopic>(a.InfoTopic, out var it) ? it : null;
        // v1.24: a missing or unknown scope is the office's (refusing a real client costs more than one offer too many).
        var scope = TryEnum<Scope>(a.Scope, out var sc) ? sc : Scope.Office;
        // The guard: a client's subject outside law and the office is off_topic, whatever the intent. Abuse keeps its
        // warning; a colleague, the press or a vendor writing to the office is the office's to decide (B-14.4).
        if (scope == Scope.Outside && intent != Intent.Abusive && role == SenderRole.Client)
        {
            intent = Intent.OffTopic;
            service = null;
        }

        // An office question without a topic it can be answered by is no office question (the offer, as for other).
        if (intent == Intent.OfficeInfo && topic is null)
        {
            return Fallback(UnknownValue, call);
        }

        return new(intent, service, mode, string.IsNullOrWhiteSpace(a.FullName) ? null : a.FullName.Trim(), a.Confidence.Value, null, call,
            matter, subtype, role, intent == Intent.OfficeInfo ? topic : null, scope, Courtesy: CourtesyMatch.FromAi(a.Courtesy),
            Gender: TryEnum<NameGender>(a.NameGender, out var g) ? g : NameGender.Unknown);
    }

    public static string Code<T>(T value)
        where T : struct, Enum => Domain.Common.StateName.Of(value);

    private static T Parse<T>(string value)
        where T : struct, Enum => Domain.Common.StateName.Parse<T>(value);

    /// <summary>Only the exact snake_case codes of the list (no numbers, no other spelling).</summary>
    private static bool TryEnum<T>(string? value, out T result)
        where T : struct, Enum
    {
        result = default;
        foreach (var v in Enum.GetValues<T>())
        {
            if (Code(v) == value)
            {
                result = v;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// v1.41 (TASK 17, part B, classify-v14): what a person reading the message would know, put before it: Hazem's last message,
/// the client's message before this one, and what he has open with the office. Text only, masked (T-09); the classifier
/// classifies the message after <see cref="Now"/>, the context only helps it understand. The fakes read the message back with
/// <see cref="MessageOf"/>.
/// </summary>
public static class AiContext
{
    public const string Head = "«سياق للفهم بس، مش رسالة العميل»";
    public const string Now = "«رسالة العميل دلوقتي»";

    public static string Build(string message, string? hazemLast, string? clientBefore, string? open)
    {
        if (hazemLast is null && clientBefore is null && open is null)
        {
            return message;
        }

        var sb = new StringBuilder(Head).Append('\n');
        if (hazemLast is not null)
        {
            sb.Append("آخر رسالة من حازم: ").Append(AiInput.Redact(hazemLast).Replace('\n', ' ')).Append('\n');
        }

        if (clientBefore is not null)
        {
            sb.Append("رسالة العميل اللي قبلها: ").Append(AiInput.Redact(clientBefore).Replace('\n', ' ')).Append('\n');
        }

        if (open is not null)
        {
            sb.Append("عنده مع المكتب: ").Append(open).Append('\n');
        }

        return sb.Append(Now).Append('\n').Append(message).ToString();
    }

    /// <summary>The client's message of a text built by <see cref="Build"/> (the text itself otherwise).</summary>
    public static string MessageOf(string text)
    {
        var at = text.IndexOf(Now + "\n", StringComparison.Ordinal);
        return at < 0 ? text : text[(at + Now.Length + 1)..];
    }
}

/// <summary>
/// The one place that builds what the AI sees (T-09): typed text, captions, voice transcripts
/// and the titles of buttons the client pressed. Never a media object, bucket, key, file name,
/// mime type, sha256 or location. Phone numbers, national IDs and e-mails are replaced first.
/// </summary>
public static partial class AiInput
{
    public const string PhoneMask = "[رقم]";
    public const string NationalIdMask = "[رقم_قومي]";
    public const string EmailMask = "[ايميل]";

    /// <summary>The redacted text of the whole turn, or null when the turn has no words (a photo only).</summary>
    public static string? Build(Turn turn)
    {
        var sb = new StringBuilder();
        foreach (var m in turn.Messages)
        {
            foreach (var part in Words(m))
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    sb.Append(part.Trim()).Append('\n');
                }
            }
        }

        return sb.Length == 0 ? null : Redact(sb.ToString().TrimEnd());
    }

    /// <summary>The client's own words in one message (no file reference fields at all).</summary>
    public static IEnumerable<string?> Words(IncomingMessage m)
    {
        yield return m.Type == MessageType.Text ? m.Text : null;
        yield return m.Caption;
        yield return m.Transcript is { Status: Transcript.Done } t ? t.Text : null;
        yield return m.Interactive?.Title;
        yield return m.Button?.Text;
    }

    public static string Redact(string text)
    {
        text = Email().Replace(text, EmailMask);
        // Arabic-Indic digits count too. A 14-digit run is a national ID; 8 or more otherwise
        // (spaces or dashes inside allowed) is a phone number.
        text = NationalId().Replace(text, NationalIdMask);
        // A date such as 12-10-2026 or 2026/10/12 is not a phone number: the AI keeps it (it can
        // point to a hearing or an urgent matter). Amounts of 8+ digits stay masked: privacy first.
        return Phone().Replace(text, m => Date().IsMatch(m.Value) ? m.Value : PhoneMask);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![0-9٠-٩])[0-9٠-٩]{14}(?![0-9٠-٩])")]
    private static partial Regex NationalId();

    [GeneratedRegex(@"(?<![0-9٠-٩])\+?[0-9٠-٩](?:[ \-]?[0-9٠-٩]){7,}(?![0-9٠-٩])")]
    private static partial Regex Phone();

    [GeneratedRegex(@"^(?:[0-9٠-٩]{1,2}-[0-9٠-٩]{1,2}-[0-9٠-٩]{4}|[0-9٠-٩]{4}-[0-9٠-٩]{1,2}-[0-9٠-٩]{1,2})$")]
    private static partial Regex Date();
}
