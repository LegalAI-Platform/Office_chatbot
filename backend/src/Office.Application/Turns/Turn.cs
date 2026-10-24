using System.Text.Json.Nodes;

namespace Office.Application.Turns;

// The turn as the application sees it, after Office.Api validated and mapped the HTTP body
// (docs/hazem-internal-api.md). Required fields are not nullable here. The property names are
// the contract's, so the stored copy in hazem.turns keeps the exact JSON shape n8n sent.

/// <summary>What the client said that needs one answer.</summary>
public sealed record Turn(string TurnId, string WaId, string? ContactName, IReadOnlyList<IncomingMessage> Messages);

public enum MessageType
{
    Text,
    Image,
    Document,
    Audio,
    Video,
    Sticker,
    Location,
    Reaction,
    Interactive,
    Button,
    Unsupported,
}

public enum MediaStatus
{
    Stored,
    Failed,
    TooLarge,
    Rejected,
}

public sealed record IncomingMessage(
    string Wamid,
    MessageType Type,
    string? Text,
    DateTimeOffset? SentAt,
    string? ContextWamid,
    string? Caption = null,
    MediaRef? Media = null,
    Transcript? Transcript = null,
    Location? Location = null,
    Reaction? Reaction = null,
    Interactive? Interactive = null,
    Button? Button = null,
    string? OriginalType = null,
    Referral? Referral = null)
{
    public bool IsMedia => Type is MessageType.Image or MessageType.Document or MessageType.Audio or MessageType.Video or MessageType.Sticker;
}

/// <summary>Where n8n stored a file. A reference only: never a URL and never the file (T-09).</summary>
public sealed record MediaRef(
    MediaStatus Status,
    string? Bucket = null,
    string? Key = null,
    string? MimeType = null,
    long? SizeBytes = null,
    string? Sha256 = null,
    string? Filename = null,
    bool? Voice = null);

public sealed record Transcript(string Status, string? Text, string? Language)
{
    public const string Done = "done";
    public const string Failed = "failed";
}

public sealed record Location(decimal? Latitude, decimal? Longitude, string? Name = null, string? Address = null);

public sealed record Reaction(string? Emoji, string? MessageWamid);

public sealed record Interactive(string? Kind, string? Id, string? Title);

public sealed record Button(string? Payload, string? Text);

/// <summary>B-13: Meta's referral when the client came from an ad or a post (no image or video links).</summary>
public sealed record Referral(string? SourceType, string? SourceId, string? Headline);

public enum ActionType
{
    SendText,
    SendTemplate,
    SendInteractive,
    SendDocument, // TASK 10: a file the lawyer delivered (B-16.6)
}

/// <summary>
/// TASK 08: WhatsApp reply buttons (up to 3, titles ≤ 20) or a list (up to 10 rows, titles ≤ 24) under
/// a text. Every title is an approved text or a value of the system (a day and a time); n8n only sends.
/// </summary>
public sealed record InteractivePayload(string Kind, string? Button, IReadOnlyList<InteractiveChoice> Choices)
{
    public const string Buttons = "buttons";
    public const string List = "list";
    public const int MaxButtons = 3;
    public const int MaxRows = 10;
    public const int MaxButtonTitle = 20;
    public const int MaxRowTitle = 24;
    public const int MaxBody = 1024;
}

public sealed record InteractiveChoice(string Id, string Title, string? Description = null);

/// <summary>Something n8n must do for this turn. Unused fields stay null.</summary>
public sealed record OutboundAction(
    ActionType Type,
    string IdempotencyKey,
    string? Text = null,
    string? ContextWamid = null,
    string? TemplateName = null,
    string? Language = null,
    JsonArray? Components = null,
    InteractivePayload? Interactive = null)
{
    /// <summary>Stable key per action, so a replayed turn can never send twice.</summary>
    public static string KeyFor(string turnId, int index) => $"hazem:{turnId}:{index}";
}

public enum TurnStatus
{
    Processed,
    Duplicate,
}

/// <summary><paramref name="Notes"/>: text keys and fallback reasons for the log, never a text.</summary>
public sealed record TurnResult(string TurnId, TurnStatus Status, IReadOnlyList<OutboundAction> Actions, IReadOnlyList<string>? Notes = null);

/// <summary>A turn as stored in hazem.turns, with who answered it and what was answered.</summary>
public sealed record StoredTurn(
    string TurnId,
    string WaId,
    string? ContactName,
    IReadOnlyList<IncomingMessage> Messages,
    string Responder,
    IReadOnlyList<OutboundAction> Actions);
