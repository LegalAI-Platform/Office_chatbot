using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Office.Core.Hazem;

// Contract: docs/hazem-internal-api.md. JSON is snake_case (turn_id, wa_id, ...).

/// <summary>What the client said that needs one answer, as sent by n8n.</summary>
public sealed record TurnRequest(string? TurnId, string? WaId, string? ContactName, IReadOnlyList<TurnMessage>? Messages);

public sealed record TurnMessage(
    string? Wamid,
    string? Type,
    string? Text,
    DateTimeOffset? SentAt,
    string? ContextWamid,
    string? Caption = null,
    TurnMedia? Media = null,
    TurnTranscript? Transcript = null,
    TurnLocation? Location = null,
    TurnReaction? Reaction = null,
    TurnInteractive? Interactive = null,
    TurnButton? Button = null,
    string? OriginalType = null);

/// <summary>Where n8n stored a file (TASK 03). Never a URL and never the file itself (T-09).</summary>
public sealed record TurnMedia(
    string? Status,
    string? Bucket = null,
    string? Key = null,
    string? MimeType = null,
    long? SizeBytes = null,
    string? Sha256 = null,
    string? Filename = null,
    bool? Voice = null);

public sealed record TurnTranscript(string? Status, string? Text, string? Language);

public sealed record TurnLocation(decimal? Latitude, decimal? Longitude, string? Name = null, string? Address = null);

public sealed record TurnReaction(string? Emoji, string? MessageWamid);

public sealed record TurnInteractive(string? Kind, string? Id, string? Title);

public sealed record TurnButton(string? Payload, string? Text);

/// <summary>Something n8n must do for this turn. Unused fields are left null.</summary>
public sealed record TurnAction(
    string Type,
    string IdempotencyKey,
    string? Text = null,
    string? ContextWamid = null,
    string? TemplateName = null,
    string? Language = null,
    JsonArray? Components = null)
{
    public const string SendText = "send_text";
    public const string SendTemplate = "send_template";
}

public sealed record TurnResponse(string TurnId, string Status, IReadOnlyList<TurnAction> Actions)
{
    public const string Processed = "processed";
    public const string Duplicate = "duplicate";
}

/// <summary>A turn as stored in hazem.turns.</summary>
public sealed record StoredTurn(
    string TurnId,
    string WaId,
    string? ContactName,
    IReadOnlyList<TurnMessage> Messages,
    string Status,
    string Responder,
    IReadOnlyList<TurnAction> Actions);

/// <summary>Outcome of one send, reported by n8n (POST /api/hazem/send-results) or by the API itself.</summary>
public sealed record SendResult(
    string? IdempotencyKey,
    bool Ok,
    string? OutboundId = null,
    string? Wamid = null,
    string? Status = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record SendResultsRequest(string? TurnId, IReadOnlyList<SendResult>? Results);

public static partial class TurnRules
{
    public const int MaxTurnIdLength = 200;
    public const int MaxMessages = 50;
    public const int MaxTextLength = 4096;
    public const int MaxResults = 100;

    /// <summary>A voice transcript can be much longer than a typed message (n8n cuts it at 16000).</summary>
    public const int MaxTranscriptLength = 16000;
    public const int MaxFilenameLength = 255;

    public static readonly IReadOnlySet<string> MessageTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "text", "image", "document", "audio", "video", "sticker",
        "location", "reaction", "interactive", "button", "unsupported",
    };

    public static readonly IReadOnlySet<string> MediaTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "image", "document", "audio", "video", "sticker",
    };

    /// <summary>Final download states; n8n never sends a turn while a download is still running.</summary>
    public static readonly IReadOnlySet<string> MediaStatuses = new HashSet<string>(StringComparer.Ordinal)
    {
        "stored", "failed", "too_large", "rejected",
    };

    public static readonly IReadOnlySet<string> Buckets = new HashSet<string>(StringComparer.Ordinal)
    {
        "office-media", "office-sensitive",
    };

    [GeneratedRegex("^[0-9]{8,15}$")]
    private static partial Regex WaIdPattern();

    [GeneratedRegex(@"^wa/[0-9]{4}/[0-9]{2}/[A-Za-z0-9._-]+$")]
    private static partial Regex MediaKeyPattern();

    private static void ValidateMessage(TurnMessage? m, string at, Dictionary<string, string[]> errors)
    {
        if (m is null || string.IsNullOrWhiteSpace(m.Wamid) || string.IsNullOrWhiteSpace(m.Type))
        {
            errors[at] = ["wamid and type are required"];
            return;
        }

        if (!MessageTypes.Contains(m.Type))
        {
            errors[$"{at}.type"] = [$"one of: {string.Join(", ", MessageTypes)}"];
        }

        if (m.Text is { Length: > MaxTextLength })
        {
            errors[$"{at}.text"] = [$"at most {MaxTextLength} characters"];
        }

        if (m.Caption is { Length: > MaxTextLength })
        {
            errors[$"{at}.caption"] = [$"at most {MaxTextLength} characters"];
        }

        var isMedia = MediaTypes.Contains(m.Type);
        if (isMedia && m.Media is null)
        {
            errors[$"{at}.media"] = ["required for media messages"];
        }
        else if (!isMedia && m.Media is not null)
        {
            errors[$"{at}.media"] = ["only media messages carry media"];
        }
        else if (m.Media is { } media)
        {
            ValidateMedia(media, $"{at}.media", errors);
        }

        if (m.Transcript is { } transcript)
        {
            if (m.Type != "audio")
            {
                errors[$"{at}.transcript"] = ["only audio messages carry a transcript"];
            }
            else if (transcript.Status is not ("done" or "failed"))
            {
                errors[$"{at}.transcript.status"] = ["done or failed"];
            }
            else if (transcript.Text is { Length: > MaxTranscriptLength })
            {
                errors[$"{at}.transcript.text"] = [$"at most {MaxTranscriptLength} characters"];
            }
        }
    }

    private static void ValidateMedia(TurnMedia media, string at, Dictionary<string, string[]> errors)
    {
        if (media.Status is null || !MediaStatuses.Contains(media.Status))
        {
            errors[$"{at}.status"] = [$"one of: {string.Join(", ", MediaStatuses)}"];
            return;
        }

        var stored = media.Status == "stored";
        if (stored && (media.Bucket is null || !Buckets.Contains(media.Bucket)))
        {
            errors[$"{at}.bucket"] = [$"one of: {string.Join(", ", Buckets)}"];
        }

        if (stored && (media.Key is null || !MediaKeyPattern().IsMatch(media.Key)))
        {
            errors[$"{at}.key"] = ["wa/yyyy/mm/<name>"];
        }

        if (!stored && (media.Bucket is not null || media.Key is not null))
        {
            errors[$"{at}.key"] = ["bucket and key only when status is stored"];
        }

        if (media.Filename is { Length: > MaxFilenameLength })
        {
            errors[$"{at}.filename"] = [$"at most {MaxFilenameLength} characters"];
        }

        // T-09: references only. No field of a file reference may hold a link. (Client-written
        // text and captions may contain links; those are just words the client typed.)
        foreach (var (name, value) in new[]
                 {
                     ("bucket", media.Bucket), ("key", media.Key), ("mime_type", media.MimeType),
                     ("sha256", media.Sha256), ("filename", media.Filename),
                 })
        {
            if (value?.Contains("://", StringComparison.Ordinal) == true)
            {
                errors[$"{at}.{name}"] = ["must not contain a URL"];
            }
        }
    }

    public static bool IsValidWaId(string? waId) => waId is not null && WaIdPattern().IsMatch(waId);

    /// <summary>Returns the validation errors (field → messages); empty when the request is valid.</summary>
    public static Dictionary<string, string[]> Validate(TurnRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["body"] = ["a JSON body is required"];
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.TurnId) || request.TurnId.Length > MaxTurnIdLength)
        {
            errors["turn_id"] = [$"required, 1-{MaxTurnIdLength} characters"];
        }

        if (!IsValidWaId(request.WaId))
        {
            errors["wa_id"] = ["required, 8-15 digits"];
        }

        if (request.Messages is not { Count: > 0 and <= MaxMessages })
        {
            errors["messages"] = [$"1-{MaxMessages} messages are required"];
        }
        else
        {
            for (var i = 0; i < request.Messages.Count; i++)
            {
                ValidateMessage(request.Messages[i], $"messages[{i}]", errors);
            }
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(SendResultsRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request?.Results is not { Count: <= MaxResults } results)
        {
            errors["results"] = [$"a list of at most {MaxResults} results is required"];
            return errors;
        }

        for (var i = 0; i < results.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(results[i]?.IdempotencyKey))
            {
                errors[$"results[{i}].idempotency_key"] = ["required"];
            }
        }

        return errors;
    }

    /// <summary>Keeps only the last 3 digits of a phone number, for logs.</summary>
    public static string Mask(string? waId) =>
        string.IsNullOrEmpty(waId) || waId.Length <= 3 ? "***" : new string('*', waId.Length - 3) + waId[^3..];
}
