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
//   /google/...                        Google Calendar (TASK 08): token, freeBusy, events; /_google/*
//   /api/sendText, /api/sendFile, /api/files/..., /api/{session}/lids/{lid}
//                                      WAHA (TASK 02b, the test channel): what n8n sends, a received file, a LID
//   GET  /_log, POST /_log/reset       what was received, for the test's assertions
// The log records whether an Authorization (or WAHA's X-Api-Key) header was present, NEVER its value: if a real
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
    // v1.41 (TASK 17, part B): the client's message comes after the context block (AiContext.Now)
    const string now = "«رسالة العميل دلوقتي»\n";
    if (text.IndexOf(now, StringComparison.Ordinal) is var cut and >= 0)
    {
        text = text[(cut + now.Length)..];
    }
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
    // TASK 06: an office question (B-20) with its topic.
    string? topic = text.Contains("المكتب فين", StringComparison.Ordinal) ? "address"
        : text.Contains("الأسعار إيه", StringComparison.Ordinal) ? "prices"
        : null;
    if (topic is not null)
    {
        (intent, name) = ("office_info", null);
    }

    var service = intent != "service_by_name" ? null
        : text.Contains("جواز عرفي", StringComparison.Ordinal) ? "urf_marriage" // TASK 11
        : "document_review"; // TASK 10
    var content = new JsonObject
    {
        ["intent"] = intent, ["service"] = service, ["consultation_mode"] = "unknown", ["full_name"] = name, ["confidence"] = 0.95,
        ["matter_type"] = text.Contains("ورث", StringComparison.Ordinal) ? "inheritance" : null,
        ["matter_subtype"] = null, ["sender_role"] = "client", ["info_topic"] = topic,
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

// TASK 08: Google Calendar for the booking (hazem-e2e sets booking.google_base_url to /google).
// The token endpoint takes any signed assertion; the calendar keeps its events in memory.
// /_google/busy adds an event of the lawyer's own, /_google/events lists them, /_google/down and
// /_google/up make it fail, /_google/reset empties it.
var calendarEvents = new ConcurrentDictionary<string, StubEvent>();
var googleDown = false;
app.MapPost("/google/token", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    log.Enqueue(Received.From(request, "google-token", null));
    return googleDown ? Results.StatusCode(503)
        : form["grant_type"] != "urn:ietf:params:oauth:grant-type:jwt-bearer" || form["assertion"].ToString().Split('.').Length != 3
            ? Results.Json(new { error = "invalid_grant" }, statusCode: 400)
            : Results.Json(new { access_token = "stub-google-token", expires_in = 3600, token_type = "Bearer" });
});
app.MapPost("/google/calendar/v3/freeBusy", async (HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    log.Enqueue(Received.From(request, "google-freebusy", null));
    if (googleDown || !request.Headers.ContainsKey("Authorization"))
    {
        return Results.StatusCode(googleDown ? 503 : 401);
    }

    var id = (string?)body?["items"]?[0]?["id"] ?? "";
    var from = DateTimeOffset.Parse((string)body!["timeMin"]!);
    var to = DateTimeOffset.Parse((string)body["timeMax"]!);
    var busy = calendarEvents.Values.Where(e => e.Start < to && from < e.End).OrderBy(e => e.Start)
        .Select(e => new JsonObject { ["start"] = e.Start.ToString("O"), ["end"] = e.End.ToString("O") }).ToArray<JsonNode?>();
    return Results.Json(new JsonObject { ["calendars"] = new JsonObject { [id] = new JsonObject { ["busy"] = new JsonArray(busy) } } });
});
app.MapPost("/google/calendar/v3/calendars/{calendarId}/events", async (string calendarId, HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    var summary = (string?)body?["summary"] ?? "";
    log.Enqueue(Received.From(request, "google-event-create", null) with { Text = summary });
    if (googleDown)
    {
        return Results.StatusCode(503);
    }

    var e = new StubEvent(Guid.NewGuid().ToString("N"), summary, DateTimeOffset.Parse((string)body!["start"]!["dateTime"]!),
        DateTimeOffset.Parse((string)body["end"]!["dateTime"]!), (string?)body["extendedProperties"]?["private"]?["hazem_appointment"]);
    calendarEvents[e.Id] = e;
    return Results.Json(new { id = e.Id });
});
app.MapMethods("/google/calendar/v3/calendars/{calendarId}/events/{eventId}", ["PATCH"], async (string calendarId, string eventId, HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    log.Enqueue(Received.From(request, "google-event-update", null) with { Text = (string?)body?["summary"] });
    if (!calendarEvents.TryGetValue(eventId, out var e))
    {
        return Results.NotFound();
    }

    calendarEvents[eventId] = e with
    {
        Summary = (string?)body?["summary"] ?? e.Summary,
        Start = DateTimeOffset.Parse((string)body!["start"]!["dateTime"]!),
        End = DateTimeOffset.Parse((string)body["end"]!["dateTime"]!),
    };
    return Results.Json(new { id = eventId });
});
app.MapDelete("/google/calendar/v3/calendars/{calendarId}/events/{eventId}", (string calendarId, string eventId, HttpRequest request) =>
{
    log.Enqueue(Received.From(request, "google-event-delete", null));
    return calendarEvents.TryRemove(eventId, out _) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/google/calendar/v3/calendars/{calendarId}/events", (string calendarId, HttpRequest request) =>
{
    log.Enqueue(Received.From(request, "google-event-list", null));
    var items = calendarEvents.Values.OrderBy(e => e.Start).Select(e => new JsonObject
    {
        ["id"] = e.Id,
        ["status"] = "confirmed",
        ["start"] = new JsonObject { ["dateTime"] = e.Start.ToString("O") },
        ["end"] = new JsonObject { ["dateTime"] = e.End.ToString("O") },
        ["extendedProperties"] = e.Appointment is null ? null
            : new JsonObject { ["private"] = new JsonObject { ["hazem_appointment"] = e.Appointment } },
    }).ToArray<JsonNode?>();
    return Results.Json(new JsonObject { ["items"] = new JsonArray(items) });
});
app.MapPost("/_google/busy", async (HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    var e = new StubEvent(Guid.NewGuid().ToString("N"), "مشغول", DateTimeOffset.Parse((string)body!["start"]!), DateTimeOffset.Parse((string)body["end"]!), null);
    calendarEvents[e.Id] = e;
    return Results.Json(new { id = e.Id });
});
app.MapGet("/_google/events", () => Results.Json(calendarEvents.Values.OrderBy(e => e.Start).ToArray()));
app.MapPost("/_google/down", () =>
{
    googleDown = true;
    return Results.Ok();
});
app.MapPost("/_google/up", () =>
{
    googleDown = false;
    return Results.Ok();
});
app.MapPost("/_google/reset", () =>
{
    calendarEvents.Clear();
    googleDown = false;
    return Results.Ok();
});

// ---------- WAHA (TASK 02b): answers like the NOWEB engine (key.id), refuses a call without X-Api-Key ----------
var wahaSent = 0;
foreach (var path in new[] { "/api/sendText", "/api/sendFile" })
{
    app.MapPost(path, async (HttpRequest request) =>
    {
        var body = JsonNode.Parse(await new StreamReader(request.Body).ReadToEndAsync());
        var keyed = request.Headers.ContainsKey("X-Api-Key");
        log.Enqueue(new Received("waha-send", request.Method, request.Path, null, keyed,
            FileName: (string?)body?["file"]?["filename"], Text: (string?)body?["text"] ?? (string?)body?["caption"]) { ChatId = (string?)body?["chatId"] });
        return keyed
            ? Results.Json(new { key = new { remoteJid = (string?)body?["chatId"], fromMe = true, id = $"STUB{Interlocked.Increment(ref wahaSent)}" }, status = "PENDING" }, statusCode: 201)
            : Results.Json(new { error = "no api key" }, statusCode: 401);
    });
}

app.MapGet("/api/files/{session}/{file}", (string session, string file, HttpRequest request) =>
{
    log.Enqueue(Received.From(request, "waha-file", file) with { AuthorizationPresent = request.Headers.ContainsKey("X-Api-Key") });
    if (!request.Headers.ContainsKey("X-Api-Key"))
    {
        return Results.StatusCode(401);
    }

    return files.TryGetValue(Path.GetFileNameWithoutExtension(file), out var f) ? Results.File(f.Bytes, f.MimeType) : Results.NotFound();
});

// A LID known to the stub: E2E…@lid is the number that follows E2E (hazem-e2e's test numbers only).
app.MapGet("/api/{session}/lids/{lid}", (string session, string lid, HttpRequest request) =>
{
    log.Enqueue(Received.From(request, "waha-lid", lid) with { AuthorizationPresent = request.Headers.ContainsKey("X-Api-Key") });
    var number = lid.StartsWith("E2E", StringComparison.Ordinal) ? lid[3..].Split('@')[0] : null;
    return Results.Json(new { lid, pn = number is { Length: >= 8 } ? number + "@c.us" : null });
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
    /// <summary>TASK 02b: the WAHA chat a message went to (a test number only).</summary>
    public string? ChatId { get; init; }

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
        : text.Contains("يراجع", StringComparison.Ordinal) ? ("service_by_name", null)
        : text.Contains("جواز عرفي", StringComparison.Ordinal) ? ("service_by_name", null)
        : text.Contains("السلام", StringComparison.Ordinal) ? ("greeting", null)
        : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4 ? ("provide_name", text.Trim())
        : ("other", null);
}

internal sealed record StubFile(string MimeType, byte[] Bytes, string AnnouncedSha256, long AnnouncedSize);

/// <summary>An event in the stub's calendar. <see cref="Appointment"/>: set on the events the API made.</summary>
internal sealed record StubEvent(string Id, string Summary, DateTimeOffset Start, DateTimeOffset End, string? Appointment);

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
