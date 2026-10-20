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
/// </summary>
public static class TurnResponder
{
    public const string None = "none";
    public const string Echo = "echo";
    public const string EchoText = "تم استلام رسالتك (اختبار) ✅";

    public static (string Responder, IReadOnlyList<TurnAction> Actions) Decide(TurnRequest turn, HazemSettings settings)
    {
        var lastText = turn.Messages?.LastOrDefault(m => m.Type == "text" && !string.IsNullOrWhiteSpace(m.Text));
        if (!settings.EchoEnabled || lastText is null || !settings.TestRecipients.Contains(turn.WaId!))
        {
            return (None, []);
        }

        return (Echo, [new TurnAction(TurnAction.SendText, ActionKey(turn.TurnId!, 0), Text: EchoText, ContextWamid: lastText.Wamid)]);
    }

    /// <summary>Stable key per action, so a replayed turn can never send twice.</summary>
    public static string ActionKey(string turnId, int index) => $"hazem:{turnId}:{index}";
}
