using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Office.Core.WhatsApp;

/// <summary>Outbound delivery statuses and their order. "failed" is terminal.</summary>
public static class OutboundStatuses
{
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Failed = "failed";
    public const string BlockedWindow = "blocked_window";
    public static readonly string[] All = [Queued, Sending, Sent, Delivered, Read, Failed, BlockedWindow];

    public static int Rank(string status) => status switch
    {
        Queued => 0,
        Sending => 1,
        Sent => 2,
        Delivered => 3,
        Read => 4,
        Failed => 99,
        _ => -1,
    };

    /// <summary>True when a status update from Meta may replace the current status.</summary>
    public static bool CanAdvance(string current, string incoming) =>
        current != Failed && current != BlockedWindow && Rank(incoming) > Rank(current);
}

/// <summary>Error codes returned by the send use cases.</summary>
public static class SendErrorCodes
{
    public const string OutsideWindow = "OUTSIDE_24H_WINDOW";
    public const string TokenInvalid = "TOKEN_INVALID";
    public const string RateLimit = "RATE_LIMIT";
    public const string Undeliverable = "UNDELIVERABLE";
    public const string RecipientNotAllowed = "RECIPIENT_NOT_ALLOWED";
    public const string TemplateNotApproved = "TEMPLATE_NOT_APPROVED";
    public const string MetaError = "META_ERROR";
    public const string NetworkOrUnknown = "NETWORK_OR_UNKNOWN";
    public const string Disabled = "WHATSAPP_DISABLED";
}

public static class MetaErrorMapper
{
    /// <summary>Maps a Graph API error code to our error code. Codes checked against the Meta docs (2026-10).</summary>
    public static string Map(int? metaCode) => metaCode switch
    {
        null => SendErrorCodes.NetworkOrUnknown,
        131047 => SendErrorCodes.OutsideWindow,
        190 => SendErrorCodes.TokenInvalid,
        130429 or 131056 or 80007 or 4 => SendErrorCodes.RateLimit,
        131026 => SendErrorCodes.Undeliverable,
        131030 => SendErrorCodes.RecipientNotAllowed,
        _ => SendErrorCodes.MetaError,
    };
}

/// <summary>The 24-hour customer service window (M-02).</summary>
public static class ContactWindowPolicy
{
    public static readonly TimeSpan Length = TimeSpan.FromHours(24);

    public static bool IsOpen(DateTimeOffset? lastInboundAt, DateTimeOffset now) =>
        lastInboundAt is { } last && now - last < Length;
}

/// <summary>Verifies Meta's X-Hub-Signature-256 header (HMAC-SHA256 of the raw body).</summary>
public static class WebhookSignature
{
    private const string Prefix = "sha256=";

    public static bool IsValid(ReadOnlySpan<byte> body, string? header, string appSecret)
    {
        if (string.IsNullOrEmpty(appSecret) || header is null || !header.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var hex = header.AsSpan(Prefix.Length);
        if (hex.Length != 64)
        {
            return false;
        }

        Span<byte> received = stackalloc byte[32];
        if (Convert.FromHexString(hex, received, out _, out var written) != System.Buffers.OperationStatus.Done || written != 32)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[32];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body, expected);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    public static string Compute(ReadOnlySpan<byte> body, string appSecret) =>
        Prefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));
}

/// <summary>Stable JSON form (object keys sorted, no whitespace) and its SHA-256, used to detect duplicate events.</summary>
public static class JsonCanonicalizer
{
    public static string Canonicalize(JsonNode? node) => Sort(node)?.ToJsonString() ?? "null";

    public static string Sha256Hex(string canonicalJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray arr => new JsonArray(arr.Select(Sort).ToArray()),
        null => null,
        _ => JsonNode.Parse(node.ToJsonString()),
    };
}

public static class PhoneMask
{
    /// <summary>2010****122 style masking for logs and alerts.</summary>
    public static string Mask(string? waId)
    {
        if (string.IsNullOrEmpty(waId))
        {
            return "";
        }

        return waId.Length <= 7 ? new string('*', waId.Length) : waId[..4] + "****" + waId[^3..];
    }
}

internal static class JsonExtensions
{
    public static string? Str(this JsonNode? node, string name) =>
        node is JsonObject o && o.TryGetPropertyValue(name, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;

    public static JsonArray? Arr(this JsonNode? node, string name) =>
        node is JsonObject o && o.TryGetPropertyValue(name, out var v) ? v as JsonArray : null;

    public static JsonObject? Obj(this JsonNode? node, string name) =>
        node is JsonObject o && o.TryGetPropertyValue(name, out var v) ? v as JsonObject : null;

    public static bool Has(this JsonNode? node, string name) =>
        node is JsonObject o && o.ContainsKey(name);

    /// <summary>Unix seconds as a string ("1760900000") or number.</summary>
    public static DateTimeOffset? UnixTime(this JsonNode? node, string name)
    {
        if (node is not JsonObject o || !o.TryGetPropertyValue(name, out var v) || v is not JsonValue jv)
        {
            return null;
        }

        if (jv.TryGetValue<string>(out var s) && long.TryParse(s, out var fromString))
        {
            return DateTimeOffset.FromUnixTimeSeconds(fromString);
        }

        return jv.TryGetValue<long>(out var n) ? DateTimeOffset.FromUnixTimeSeconds(n) : null;
    }

    public static string? Json(this JsonNode? node) => node?.ToJsonString(JsonSerializerOptions.Default);
}
