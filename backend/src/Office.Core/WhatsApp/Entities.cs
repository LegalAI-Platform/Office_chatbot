namespace Office.Core.WhatsApp;

/// <summary>One "change" received from Meta (the change value plus its field name).</summary>
public sealed class WebhookEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? Field { get; set; }
    public string? PhoneNumberId { get; set; }
    public string Payload { get; set; } = "{}";
    public string PayloadSha256 { get; set; } = "";
    public bool SignatureValid { get; set; }
    public string ProcessingStatus { get; set; } = WebhookEventStatuses.Received;
    public string? Error { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public static class WebhookEventStatuses
{
    public const string Received = "received";
    public const string Processed = "processed";
    public const string Ignored = "ignored";
    public const string Failed = "failed";
    public static readonly string[] All = [Received, Processed, Ignored, Failed];
}

/// <summary>A message a customer sent to the office number. TASK 03 picks up rows in "pending".</summary>
public sealed class InboundMessage
{
    public Guid Id { get; set; }
    public string Wamid { get; set; } = "";
    public string WaId { get; set; } = "";
    public string? ContactName { get; set; }
    public string PhoneNumberId { get; set; } = "";
    public string MessageType { get; set; } = "";
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? ContextWamid { get; set; }
    public string? Referral { get; set; }
    public bool IsGroup { get; set; }
    public string Raw { get; set; } = "{}";
    public Guid? WebhookEventId { get; set; }
    public string ProcessingStatus { get; set; } = InboundStatuses.Pending;
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class InboundStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Processed = "processed";
    public const string IgnoredGroup = "ignored_group";
    public const string IgnoredUnsupported = "ignored_unsupported";
    public const string Failed = "failed";
    public static readonly string[] All = [Pending, Processing, Processed, IgnoredGroup, IgnoredUnsupported, Failed];
}

/// <summary>Last time a customer wrote to us; drives the 24-hour service window (M-02).</summary>
public sealed class ContactWindow
{
    public string WaId { get; set; } = "";
    public DateTimeOffset LastInboundAt { get; set; }
    public string? LastInboundWamid { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OutboundMessage
{
    public Guid Id { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string WaId { get; set; } = "";
    public string Kind { get; set; } = OutboundKinds.Text;
    public string? TemplateName { get; set; }
    public string? TemplateLanguage { get; set; }
    public string RequestPayload { get; set; } = "{}";
    public string? ResponsePayload { get; set; }
    public string? Wamid { get; set; }
    public string Status { get; set; } = OutboundStatuses.Queued;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class OutboundKinds
{
    public const string Text = "text";
    public const string Template = "template";
    public const string Media = "media";
    public static readonly string[] All = [Text, Template, Media];
}

public sealed class MessageStatus
{
    public Guid Id { get; set; }
    public string Wamid { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset StatusAt { get; set; }
    public string? RecipientId { get; set; }
    public string? Errors { get; set; }
    public string? Pricing { get; set; }
    public string Raw { get; set; } = "{}";
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class WhatsAppTemplate
{
    public string Name { get; set; } = "";
    public string Language { get; set; } = "";
    public string? Category { get; set; }
    public string? Status { get; set; }
    public string? MetaTemplateId { get; set; }
    public string? Components { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
