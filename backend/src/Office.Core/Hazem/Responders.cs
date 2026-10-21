namespace Office.Core.Hazem;

/// <summary>Non-secret API settings from hazem.settings.</summary>
public sealed record HazemSettings(IReadOnlyDictionary<string, string> Values)
{
    public const string EchoEnabledKey = "echo.enabled";
    public const string EchoRecipientsKey = "echo.recipients";

    public bool EchoEnabled => Values.TryGetValue(EchoEnabledKey, out var v) && v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Numbers that get the echo reply, and the only numbers the CLI send-test may write to.</summary>
    public IReadOnlySet<string> TestRecipients =>
        (Values.TryGetValue(EchoRecipientsKey, out var v) ? v : "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Decides what to answer. Until TASK 04 (intent classification) the only answer is the
/// test echo, and only for the numbers in echo.recipients while echo.enabled is true.
/// The echo proves the turn's shape arrived: a voice transcript, then media, then text.
/// </summary>
public static class TurnResponder
{
    public const string None = "none";
    public const string Echo = "echo";
    public const string EchoText = "تم استلام رسالتك (اختبار) ✅";
    public const string EchoVoicePrefix = "تم استلام رسالتك الصوتية (اختبار): ";
    public const int EchoTranscriptChars = 60;

    private static readonly IReadOnlyDictionary<string, string> MediaNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["image"] = "الصورة",
        ["document"] = "المستند",
        ["audio"] = "الرسالة الصوتية",
        ["video"] = "الفيديو",
        ["sticker"] = "الملصق",
    };

    public static (string Responder, IReadOnlyList<TurnAction> Actions) Decide(TurnRequest turn, HazemSettings settings)
    {
        var messages = turn.Messages ?? [];
        if (!settings.EchoEnabled || messages.Count == 0 || !settings.TestRecipients.Contains(turn.WaId!))
        {
            return (None, []);
        }

        var transcript = messages.LastOrDefault(m => m.Transcript is { Status: "done", Text: { Length: > 0 } })?.Transcript!.Text;
        var media = messages.LastOrDefault(m => m.Type is not null && TurnRules.MediaTypes.Contains(m.Type));
        var hasText = messages.Any(m => m.Type == "text" && !string.IsNullOrWhiteSpace(m.Text));

        string text;
        if (transcript is not null)
        {
            text = EchoVoicePrefix + (transcript.Length > EchoTranscriptChars ? transcript[..EchoTranscriptChars] : transcript);
        }
        else if (media is not null)
        {
            text = $"تم استلام {MediaNames[media.Type!]} (اختبار) ✅";
        }
        else if (hasText)
        {
            text = EchoText;
        }
        else
        {
            return (None, []);
        }

        return (Echo, [new TurnAction(TurnAction.SendText, ActionKey(turn.TurnId!, 0), Text: text, ContextWamid: messages[^1].Wamid)]);
    }

    /// <summary>Stable key per action, so a replayed turn can never send twice.</summary>
    public static string ActionKey(string turnId, int index) => $"hazem:{turnId}:{index}";
}
