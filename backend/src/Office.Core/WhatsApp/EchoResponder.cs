namespace Office.Core.WhatsApp;

/// <summary>
/// Test-only auto reply. Replies once to pending text messages from TestRecipients, and
/// only while EchoTest is enabled. It does not change the message's processing status.
/// </summary>
public sealed class EchoResponder(IWhatsAppStore store, MessageSender sender, IWhatsAppSettings settings)
{
    public const string ReplyText = "تم استلام رسالتك (اختبار) ✅";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        if (!settings.Enabled || !settings.EchoTestEnabled || settings.TestRecipients.Count == 0)
        {
            return 0;
        }

        var replied = 0;
        foreach (var message in await store.GetPendingTextMessagesAsync(settings.TestRecipients, 50, ct))
        {
            var result = await sender.SendTextAsync(message.WaId, ReplyText, "echo:" + message.Wamid, "echo-test", message.Wamid, ct);
            if (result.Ok && !result.Duplicate)
            {
                replied++;
            }
        }

        return replied;
    }
}
