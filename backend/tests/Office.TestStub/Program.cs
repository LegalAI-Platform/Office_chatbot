using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

// Test-only stand-in for the two outside services TASK 03 calls (hazem-e2e points
// whatsapp.graph_base_url and ai.openai_base_url here):
//   GET  /{version}/{media_id}         Graph API media metadata (url, mime_type, sha256, file_size)
//   GET  /files/{media_id}             the file behind that url
//   POST /v1/audio/transcriptions      OpenAI transcription (multipart), fixed Arabic text
//   POST /v1/chat/completions          OpenAI classification (TASK 04), keyword answers; /_ai/down, /_ai/up
//   GET  /_log, POST /_log/reset       what was received, for the test's assertions
// The log records whether an Authorization header was present, NEVER its value: if a real
// credential exists in n8n, its value would reach this container.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

var log = new ConcurrentQueue<Received>();
var files = StubFiles.All;

app.MapGet("/{version}/{mediaId}", (string version, string mediaId, HttpRequest request) =>
{
    log.Enqueue(Received.From(request, "media-info", mediaId));
    if (!request.Headers.ContainsKey("Authorization"))
    {
        return Results.Json(new { error = new { code = 190, message = "no token" } }, statusCode: 401);
    }

    if (!files.TryGetValue(mediaId, out var f))
    {
        return Results.Json(new { error = new { code = 100, message = "unknown media" } }, statusCode: 404);
    }

    return Results.Json(new
    {
        messaging_product = "whatsapp",
        id = mediaId,
        url = $"http://meta-stub:8080/files/{mediaId}?sig=stub",
        mime_type = f.MimeType,
        sha256 = f.AnnouncedSha256,
        file_size = f.AnnouncedSize,
    });
});

app.MapGet("/files/{mediaId}", (string mediaId, HttpRequest request) =>
{
    log.Enqueue(Received.From(request, "media-download", mediaId));
    if (!request.Headers.ContainsKey("Authorization"))
    {
        return Results.StatusCode(401);
    }

    return files.TryGetValue(mediaId, out var f) ? Results.File(f.Bytes, f.MimeType) : Results.NotFound();
});

app.MapPost("/v1/audio/transcriptions", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    byte[] bytes = [];
    if (file is not null)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        bytes = ms.ToArray();
    }

    log.Enqueue(Received.From(request, "transcription", null) with
    {
        FileName = file?.FileName,
        FileContentType = file?.ContentType,
        FileSize = bytes.Length,
        FileSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        FileIsOgg = bytes.Length >= 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "OggS",
        Model = form["model"].ToString(),
        Language = form["language"].ToString(),
    });

    if (!request.Headers.ContainsKey("Authorization"))
    {
        return Results.Json(new { error = new { message = "no key" } }, statusCode: 401);
    }

    return file is null
        ? Results.Json(new { error = new { message = "file is required" } }, statusCode: 400)
        : Results.Json(new { text = StubFiles.Transcript });
});

// TASK 04: OpenAI Chat Completions with Structured Outputs, for the .NET classifier. Answers a
// fixed classification by keywords and records the user text it got (to prove what the AI saw).
var aiDown = false;
app.MapPost("/v1/chat/completions", async (HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    var text = (string?)body?["messages"]?[1]?["content"] ?? "";
    log.Enqueue(Received.From(request, "classification", null) with
    {
        Model = (string?)body?["model"],
        Text = text,
        Strict = (bool?)body?["response_format"]?["json_schema"]?["strict"],
    });

    if (!request.Headers.ContainsKey("Authorization"))
    {
        return Results.Json(new { error = new { message = "no key" } }, statusCode: 401);
    }

    if (aiDown)
    {
        return Results.Json(new { error = new { message = "stub is down" } }, statusCode: 503);
    }

    var (intent, name) = StubAi.Classify(text);
    var content = new JsonObject
    {
        ["intent"] = intent, ["service"] = null, ["consultation_mode"] = "unknown", ["full_name"] = name, ["confidence"] = 0.95,
        ["matter_type"] = text.Contains("ورث", StringComparison.Ordinal) ? "inheritance" : null,
        ["matter_subtype"] = null, ["sender_role"] = "client",
    }.ToJsonString();
    return Results.Json(new
    {
        choices = new[] { new { message = new { role = "assistant", content } } },
        usage = new { prompt_tokens = 500, completion_tokens = 25 },
    });
});
app.MapPost("/_ai/down", () =>
{
    aiDown = true;
    return Results.Ok();
});
app.MapPost("/_ai/up", () =>
{
    aiDown = false;
    return Results.Ok();
});

app.MapGet("/_log", () => Results.Json(log.ToArray()));
app.MapPost("/_log/reset", () =>
{
    log.Clear();
    return Results.Ok();
});
app.MapGet("/health", () => "ok");

app.Run();

internal sealed record Received(
    string Kind,
    string Method,
    string Path,
    string? MediaId,
    bool AuthorizationPresent,
    string? FileName = null,
    string? FileContentType = null,
    long? FileSize = null,
    string? FileSha256 = null,
    bool? FileIsOgg = null,
    string? Model = null,
    string? Language = null,
    string? Text = null,
    bool? Strict = null)
{
    public static Received From(HttpRequest r, string kind, string? mediaId) =>
        new(kind, r.Method, r.Path, mediaId, r.Headers.ContainsKey("Authorization"));
}

/// <summary>Keyword classification for hazem-e2e (the real model is never called in tests).</summary>
internal static class StubAi
{
    public static (string Intent, string? Name) Classify(string text) =>
        text.Contains("الجلسة", StringComparison.Ordinal) ? ("existing_matter", null)
        : text.Contains("ورث", StringComparison.Ordinal) ? ("describe_matter", null)
        : text.Contains("أطلق", StringComparison.Ordinal) ? ("legal_question", null)
        : text.Contains("أحجز", StringComparison.Ordinal) ? ("consultation_request", null)
        : text.Contains("السلام", StringComparison.Ordinal) ? ("greeting", null)
        : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4 ? ("provide_name", text.Trim())
        : ("other", null);
}

internal sealed record StubFile(string MimeType, byte[] Bytes, string AnnouncedSha256, long AnnouncedSize);

/// <summary>Small fixed files. Media ids are what hazem-e2e puts in its fake Meta events.</summary>
internal static class StubFiles
{
    public const string Transcript = "عايز أحجز ميعاد استشارة يوم الخميس الساعة خمسة";

    public static readonly IReadOnlyDictionary<string, StubFile> All = Build();

    private static Dictionary<string, StubFile> Build()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9 };
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");
        var ogg = Encoding.ASCII.GetBytes("OggS\0\u0002stub-voice-note-opus-data");
        var exe = Encoding.ASCII.GetBytes("MZ-stub-executable");

        StubFile Exact(string mime, byte[] bytes) => new(mime, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);

        return new Dictionary<string, StubFile>(StringComparer.Ordinal)
        {
            ["STUB-IMG"] = Exact("image/jpeg", jpeg),
            ["STUB-PDF"] = Exact("application/pdf", pdf),
            ["STUB-OGG"] = Exact("audio/ogg; codecs=opus", ogg),
            // Meta announces a sha256 that does not match the bytes.
            ["STUB-BADSHA"] = Exact("image/jpeg", jpeg) with { AnnouncedSha256 = new string('0', 64) },
            // Meta announces 70 MB: never downloaded.
            ["STUB-BIG"] = Exact("application/pdf", pdf) with { AnnouncedSize = 70L * 1024 * 1024 },
            // Not an allowed type: rejected, never downloaded.
            ["STUB-EXE"] = Exact("application/x-msdownload", exe),
        };
    }
}
