using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Office.Core.Hazem;

// Contract: docs/hazem-internal-api.md. JSON is snake_case (turn_id, wa_id, ...).

/// <summary>What the client said that needs one answer, as sent by n8n.</summary>
public sealed record TurnRequest(string? TurnId, string? WaId, string? ContactName, IReadOnlyList<TurnMessage>? Messages);

public sealed record TurnMessage(string? Wamid, string? Type, string? Text, DateTimeOffset? SentAt, string? ContextWamid);

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

    [GeneratedRegex("^[0-9]{8,15}$")]
    private static partial Regex WaIdPattern();

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
                var m = request.Messages[i];
                if (m is null || string.IsNullOrWhiteSpace(m.Wamid) || string.IsNullOrWhiteSpace(m.Type))
                {
                    errors[$"messages[{i}]"] = ["wamid and type are required"];
                }
                else if (m.Text is { Length: > MaxTextLength })
                {
                    errors[$"messages[{i}].text"] = [$"at most {MaxTextLength} characters"];
                }
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
