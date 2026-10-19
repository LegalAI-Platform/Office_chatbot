namespace Office.Core.WhatsApp;

/// <summary>Persistence for the WhatsApp module. Implemented in Office.Infrastructure.</summary>
public interface IWhatsAppStore
{
    /// <summary>Runs <paramref name="work"/> inside one database transaction.</summary>
    Task InTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct);

    /// <summary>Inserts the event unless an event with the same hash exists. Returns the new id, or null for a duplicate.</summary>
    Task<Guid?> TryAddWebhookEventAsync(WebhookEvent evt, CancellationToken ct);
    Task<WebhookEvent?> GetWebhookEventAsync(Guid id, CancellationToken ct);
    Task MarkWebhookEventAsync(Guid id, string status, string? errorJson, DateTimeOffset at, CancellationToken ct);

    /// <summary>Inserts the message unless its wamid exists. Returns true when inserted.</summary>
    Task<bool> TryAddInboundMessageAsync(InboundMessage message, CancellationToken ct);

    /// <summary>Moves the window forward only (never backwards).</summary>
    Task OpenContactWindowAsync(string waId, DateTimeOffset at, string wamid, CancellationToken ct);
    Task<DateTimeOffset?> GetLastInboundAtAsync(string waId, CancellationToken ct);

    Task<bool> TryAddMessageStatusAsync(MessageStatus status, CancellationToken ct);

    /// <summary>Applies a Meta status to the outbound message with that wamid, respecting the status order.</summary>
    Task AdvanceOutboundStatusAsync(string wamid, string status, string? errorsJson, CancellationToken ct);

    Task UpsertTemplateStatusAsync(string name, string language, string? status, string? metaTemplateId, string? reason, DateTimeOffset at, CancellationToken ct);
    Task UpsertTemplateAsync(WhatsAppTemplate template, CancellationToken ct);
    Task<bool> IsTemplateApprovedAsync(string name, string language, CancellationToken ct);

    /// <summary>Inserts the outbound row unless the idempotency key exists. Returns false for a duplicate.</summary>
    Task<bool> TryAddOutboundAsync(OutboundMessage message, CancellationToken ct);
    Task<OutboundMessage?> GetOutboundByKeyAsync(string idempotencyKey, CancellationToken ct);
    Task SaveOutboundAsync(OutboundMessage message, CancellationToken ct);

    Task<IReadOnlyList<InboundMessage>> GetPendingTextMessagesAsync(IReadOnlyCollection<string> fromWaIds, int limit, CancellationToken ct);
}

/// <summary>Meta Graph API. Implemented in Office.Infrastructure.</summary>
public interface IWhatsAppCloudApi
{
    Task<CloudApiResult> SendMessageAsync(string requestJson, CancellationToken ct);
}

public sealed record CloudApiResult(bool Ok, string? Wamid, string? ResponseJson, int? MetaCode, string? ErrorMessage, string? ErrorJson)
{
    public string ErrorCode => Ok ? "" : MetaErrorMapper.Map(MetaCode);
}

/// <summary>Internal alerts for the operator (Telegram or no-op). Never include customer data.</summary>
public interface IOpsNotifier
{
    Task NotifyAsync(string message, CancellationToken ct);
}

/// <summary>Non-secret WhatsApp settings the use cases need.</summary>
public interface IWhatsAppSettings
{
    bool Enabled { get; }
    IReadOnlyCollection<string> TestRecipients { get; }
    bool EchoTestEnabled { get; }
}
