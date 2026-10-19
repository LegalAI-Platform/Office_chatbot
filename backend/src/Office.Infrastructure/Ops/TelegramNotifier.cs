using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Office.Core.WhatsApp;

namespace Office.Infrastructure.Ops;

public sealed class TelegramOptions
{
    public const string Section = "Telegram";
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    public bool IsConfigured => BotToken.Length > 0 && ChatId.Length > 0;
}

/// <summary>Operator alerts (T-16). Messages must never contain customer data. Without config it only logs.</summary>
public sealed class TelegramNotifier(HttpClient http, IOptions<TelegramOptions> options, ILogger<TelegramNotifier> logger) : IOpsNotifier
{
    public const string HttpClientName = "telegram";

    public async Task NotifyAsync(string message, CancellationToken ct)
    {
        logger.LogWarning("Ops alert: {Alert}", message);
        var o = options.Value;
        if (!o.IsConfigured)
        {
            return;
        }

        try
        {
            using var response = await http.PostAsJsonAsync($"bot{o.BotToken}/sendMessage",
                new { chat_id = o.ChatId, text = "⚠️ Office WhatsApp: " + message }, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram alert failed with HTTP {Status}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Telegram alert failed: {Error}", ex.GetType().Name);
        }
    }
}
