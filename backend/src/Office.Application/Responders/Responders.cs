using Office.Application.Conversations;
using Office.Application.Turns;

namespace Office.Application.Responders;

/// <summary>Non-secret API settings from hazem.settings.</summary>
public sealed record HazemSettings(IReadOnlyDictionary<string, string> Values)
{
    public const string EchoEnabledKey = "echo.enabled";
    public const string EchoRecipientsKey = "echo.recipients";

    public bool EchoEnabled => Flag(EchoEnabledKey);

    /// <summary>Numbers that get the echo reply, and the only numbers the CLI send-test may write to.</summary>
    public IReadOnlySet<string> TestRecipients => List(EchoRecipientsKey);

    public string Get(string key, string fallback = "") => Values.TryGetValue(key, out var v) ? v.Trim() : fallback;

    public bool Flag(string key) => Get(key).Equals("true", StringComparison.OrdinalIgnoreCase);

    public IReadOnlySet<string> List(string key) =>
        Get(key).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
}

/// <summary>What to answer for one turn, which responder decided it, and what else it changes.</summary>
public sealed record TurnDecision(
    string Responder,
    IReadOnlyList<OutboundAction> Actions,
    TurnEffects? Effects = null,
    IReadOnlyList<string>? Notes = null)
{
    public const string None = "none";

    public static TurnDecision Nothing { get; } = new(None, []);
}

/// <summary>Decides what to answer. Replaceable, so tests can use a fake.</summary>
public interface ITurnResponder
{
    string Name { get; }

    Task<TurnDecision> DecideAsync(Turn turn, HazemSettings settings, CancellationToken ct);
}

/// <summary>Picks the responder named in hazem.settings.</summary>
public interface IResponderSelector
{
    ITurnResponder Select(HazemSettings settings);
}

/// <summary>
/// The test echo (TASK 02/03): answers only the numbers in echo.recipients while echo.enabled is
/// true. It proves the turn's shape arrived: a voice transcript, then media, then text.
/// </summary>
public sealed class EchoResponder : ITurnResponder
{
    public const string ResponderName = "echo";
    public const string EchoText = "تم استلام رسالتك (اختبار) ✅";
    public const string EchoVoicePrefix = "تم استلام رسالتك الصوتية (اختبار): ";
    public const int EchoTranscriptChars = 60;

    private static readonly IReadOnlyDictionary<MessageType, string> MediaNames = new Dictionary<MessageType, string>
    {
        [MessageType.Image] = "الصورة",
        [MessageType.Document] = "المستند",
        [MessageType.Audio] = "الرسالة الصوتية",
        [MessageType.Video] = "الفيديو",
        [MessageType.Sticker] = "الملصق",
    };

    public string Name => ResponderName;

    public Task<TurnDecision> DecideAsync(Turn turn, HazemSettings settings, CancellationToken ct) =>
        Task.FromResult(Decide(turn, settings));

    public static TurnDecision Decide(Turn turn, HazemSettings settings)
    {
        var messages = turn.Messages;
        if (!settings.EchoEnabled || messages.Count == 0 || !settings.TestRecipients.Contains(turn.WaId))
        {
            return TurnDecision.Nothing;
        }

        var transcript = messages.LastOrDefault(m => m.Transcript is { Status: Transcript.Done, Text.Length: > 0 })?.Transcript!.Text!;
        var media = messages.LastOrDefault(m => m.IsMedia);
        var hasText = messages.Any(m => m.Type == MessageType.Text && !string.IsNullOrWhiteSpace(m.Text));

        string text;
        if (transcript is not null)
        {
            text = EchoVoicePrefix + (transcript.Length > EchoTranscriptChars ? transcript[..EchoTranscriptChars] : transcript);
        }
        else if (media is not null)
        {
            text = $"تم استلام {MediaNames[media.Type]} (اختبار) ✅";
        }
        else if (hasText)
        {
            text = EchoText;
        }
        else
        {
            return TurnDecision.Nothing;
        }

        return new TurnDecision(ResponderName,
            [new OutboundAction(ActionType.SendText, OutboundAction.KeyFor(turn.TurnId, 0), Text: text, ContextWamid: messages[^1].Wamid)]);
    }
}
