using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Office.Application.Booking;
using Office.Application.Responders;
using Office.Domain.Booking;

namespace Office.Infrastructure.Booking;

/// <summary>An event of the lawyer's calendar. <see cref="AppointmentId"/>: set on the events Hazem made.</summary>
public sealed record CalendarEvent(string Id, DateTimeOffset Start, DateTimeOffset End, Guid? AppointmentId);

/// <summary>
/// The lawyer's Google Calendar (T-14, P2): a Service Account the owner made, the calendar shared with
/// it. Only .NET talks to Google. The key (GOOGLE_SERVICE_ACCOUNT_JSON) is never logged; this
/// HttpClient has no loggers. Every call: 10 seconds, one retry for a temporary error.
/// </summary>
public sealed class GoogleCalendar(HttpClient http, IOptions<HazemOptions> options, IHostEnvironment env, TimeProvider clock) : IBusyCalendar
{
    public const string HttpClientName = "google-calendar";
    public const string DefaultBaseUrl = "https://www.googleapis.com";
    public const string Scope = "https://www.googleapis.com/auth/calendar";
    public const string AppointmentProperty = "hazem_appointment";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Until)> Tokens = new();

    private sealed record Account(string Email, string PrivateKeyPem, string TokenUri);

    /// <summary>GOOGLE_SERVICE_ACCOUNT_JSON holds a readable key (its value is never shown).</summary>
    public bool HasKey => ReadAccount() is not null;

    /// <summary>The service account's e-mail (not a secret): the lawyer shares his calendar with it.</summary>
    public string? ServiceAccountEmail => ReadAccount()?.Email;

    /// <summary>
    /// The stub (hazem-e2e) takes any signed assertion: with no key in .env, a key made in memory for
    /// this run signs it. Only in the test environment with the stub's address set, never in production.
    /// </summary>
    private static readonly Lazy<Account> StubAccount = new(() =>
    {
        using var rsa = RSA.Create(2048);
        return new Account("hazem-e2e@stub.invalid", rsa.ExportPkcs8PrivateKeyPem(), "");
    });

    private Account? AccountFor(HazemSettings settings) => ReadAccount() ?? (UseStub(settings) ? StubAccount.Value : null);

    /// <summary>Configured = a key in .env and a calendar id in the panel.</summary>
    public bool IsConfigured(HazemSettings settings) => AccountFor(settings) is not null && settings.Get(BookingSettings.CalendarIdKey).Length > 0;

    public async Task<CalendarBusy> BusyAsync(DateTimeOffset from, DateTimeOffset to, HazemSettings settings, CancellationToken ct)
    {
        var calendarId = settings.Get(BookingSettings.CalendarIdKey);
        if (AccountFor(settings) is null || calendarId.Length == 0)
        {
            return CalendarBusy.Unavailable("not_configured");
        }

        var body = new JsonObject
        {
            ["timeMin"] = from.ToUniversalTime().ToString("O"),
            ["timeMax"] = to.ToUniversalTime().ToString("O"),
            ["items"] = new JsonArray(new JsonObject { ["id"] = calendarId }),
        };
        var (status, json) = await CallAsync(HttpMethod.Post, "/calendar/v3/freeBusy", body, settings, ct);
        if (status != HttpStatusCode.OK || json?["calendars"]?[calendarId] is not JsonObject cal)
        {
            return CalendarBusy.Unavailable(status is null ? "unreachable" : $"http_{(int)status}");
        }

        if (cal["errors"] is JsonArray { Count: > 0 })
        {
            return CalendarBusy.Unavailable("calendar_error"); // e.g. not shared with the service account
        }

        var busy = new List<BusyInterval>();
        foreach (var b in cal["busy"] as JsonArray ?? [])
        {
            if (DateTimeOffset.TryParse((string?)b?["start"], out var s) && DateTimeOffset.TryParse((string?)b?["end"], out var e))
            {
                busy.Add(new BusyInterval(s, e));
            }
        }

        return new CalendarBusy(true, busy);
    }

    /// <summary>The event's id, or null when Google refused or was unreachable (the worker tries again).</summary>
    public async Task<string?> CreateEventAsync(HazemSettings settings, string summary, DateTimeOffset start, DateTimeOffset end, Guid appointmentId,
        CancellationToken ct)
    {
        var (status, json) = await CallAsync(HttpMethod.Post, $"/calendar/v3/calendars/{Uri.EscapeDataString(settings.Get(BookingSettings.CalendarIdKey))}/events",
            EventBody(summary, start, end, appointmentId), settings, ct);
        return status == HttpStatusCode.OK ? (string?)json?["id"] : null;
    }

    public async Task<bool> UpdateEventAsync(HazemSettings settings, string eventId, string summary, DateTimeOffset start, DateTimeOffset end,
        Guid appointmentId, CancellationToken ct)
    {
        var (status, _) = await CallAsync(HttpMethod.Patch, EventPath(settings, eventId), EventBody(summary, start, end, appointmentId), settings, ct);
        return status == HttpStatusCode.OK;
    }

    /// <summary>True also when the event is already gone (404 / 410): the lawyer may have deleted it.</summary>
    public async Task<bool> DeleteEventAsync(HazemSettings settings, string eventId, CancellationToken ct)
    {
        var (status, _) = await CallAsync(HttpMethod.Delete, EventPath(settings, eventId), null, settings, ct);
        return status is HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound or HttpStatusCode.Gone;
    }

    /// <summary>The busy events in [from, to) (cancelled and «free» events left out); null when unreachable.</summary>
    public async Task<IReadOnlyList<CalendarEvent>?> ListEventsAsync(HazemSettings settings, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var events = new List<CalendarEvent>();
        string? page = null;
        for (var i = 0; i < 10; i++)
        {
            var query = $"?singleEvents=true&orderBy=startTime&maxResults=250&timeMin={Uri.EscapeDataString(from.ToUniversalTime().ToString("O"))}"
                + $"&timeMax={Uri.EscapeDataString(to.ToUniversalTime().ToString("O"))}" + (page is null ? "" : $"&pageToken={Uri.EscapeDataString(page)}");
            var (status, json) = await CallAsync(HttpMethod.Get,
                $"/calendar/v3/calendars/{Uri.EscapeDataString(settings.Get(BookingSettings.CalendarIdKey))}/events{query}", null, settings, ct);
            if (status != HttpStatusCode.OK)
            {
                return null;
            }

            foreach (var item in json?["items"] as JsonArray ?? [])
            {
                if ((string?)item?["status"] == "cancelled" || (string?)item?["transparency"] == "transparent")
                {
                    continue;
                }

                if (Time(item?["start"]) is { } s && Time(item?["end"]) is { } e)
                {
                    var own = (string?)item?["extendedProperties"]?["private"]?[AppointmentProperty];
                    events.Add(new CalendarEvent((string?)item?["id"] ?? "", s, e, Guid.TryParse(own, out var g) ? g : null));
                }
            }

            page = (string?)json?["nextPageToken"];
            if (page is null)
            {
                break;
            }
        }

        return events;
    }

    /// <summary>A timed event (dateTime), or an all-day one (date, the whole Cairo day: the lawyer is away).</summary>
    private static DateTimeOffset? Time(JsonNode? node)
    {
        if (DateTimeOffset.TryParse((string?)node?["dateTime"], out var t))
        {
            return t;
        }

        return DateOnly.TryParse((string?)node?["date"], out var d) ? BookingDesk.Instant(d) : null;
    }

    private static string EventPath(HazemSettings settings, string eventId) =>
        $"/calendar/v3/calendars/{Uri.EscapeDataString(settings.Get(BookingSettings.CalendarIdKey))}/events/{Uri.EscapeDataString(eventId)}";

    private static JsonObject EventBody(string summary, DateTimeOffset start, DateTimeOffset end, Guid appointmentId) => new()
    {
        ["summary"] = summary,
        ["start"] = new JsonObject { ["dateTime"] = start.ToUniversalTime().ToString("O"), ["timeZone"] = "Africa/Cairo" },
        ["end"] = new JsonObject { ["dateTime"] = end.ToUniversalTime().ToString("O"), ["timeZone"] = "Africa/Cairo" },
        ["extendedProperties"] = new JsonObject { ["private"] = new JsonObject { [AppointmentProperty] = appointmentId.ToString() } },
    };

    /// <summary>One call within <see cref="CallBudget"/>, retried once on 429 / 5xx / network. Null status = unreachable.</summary>
    private async Task<(HttpStatusCode? Status, JsonNode? Json)> CallAsync(HttpMethod method, string path, JsonNode? body, HazemSettings settings,
        CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(CallBudget);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var token = await TokenAsync(settings, budget.Token);
                if (token is null)
                {
                    return (null, null);
                }

                using var request = new HttpRequestMessage(method, BaseUrl(settings) + path);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body);
                }

                using var response = await http.SendAsync(request, budget.Token);
                if (attempt == 1 && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
                {
                    continue;
                }

                var text = await response.Content.ReadAsStringAsync(budget.Token);
                return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                if (attempt >= 2 || budget.IsCancellationRequested)
                {
                    return (null, null);
                }
            }
        }
    }

    /// <summary>OAuth 2.0 for a service account: a JWT signed with its key, swapped for an hour's token.</summary>
    private async Task<string?> TokenAsync(HazemSettings settings, CancellationToken ct)
    {
        if (AccountFor(settings) is not { } account)
        {
            return null;
        }

        var tokenUrl = UseStub(settings) ? BaseUrl(settings) + "/token" : account.TokenUri;
        var cacheKey = account.Email + "|" + tokenUrl;
        var now = clock.GetUtcNow();
        if (Tokens.TryGetValue(cacheKey, out var cached) && cached.Until > now.AddMinutes(1))
        {
            return cached.Token;
        }

        var assertion = Jwt(account, tokenUrl, now);
        using var response = await http.PostAsync(tokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion,
        }), ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        if ((string?)json?["access_token"] is not { Length: > 0 } token)
        {
            return null;
        }

        var seconds = json["expires_in"] is JsonValue v && v.TryGetValue<int>(out var e) ? e : 3600;
        Tokens[cacheKey] = (token, now.AddSeconds(seconds));
        return token;
    }

    internal static string Jwt(string email, string privateKeyPem, string audience, DateTimeOffset now)
    {
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = email,
            ["scope"] = Scope,
            ["aud"] = audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{B64(signature)}";
    }

    private static string Jwt(Account a, string audience, DateTimeOffset now) => Jwt(a.Email, a.PrivateKeyPem, audience, now);

    /// <summary>GOOGLE_SERVICE_ACCOUNT_JSON: the key file Google gives, as base64 (or the JSON itself).</summary>
    private Account? ReadAccount()
    {
        var raw = options.Value.GoogleServiceAccountJson.Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        try
        {
            var text = raw.StartsWith('{') ? raw : Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            var json = JsonNode.Parse(text);
            var email = (string?)json?["client_email"];
            var key = (string?)json?["private_key"];
            var tokenUri = (string?)json?["token_uri"] ?? "https://oauth2.googleapis.com/token";
            return email is { Length: > 0 } && key is { Length: > 0 } ? new Account(email, key, tokenUri) : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The stub is honoured only in the test environment (ENVIRONMENT=test), never in production.</summary>
    private bool UseStub(HazemSettings settings) => env.IsEnvironment("test") && settings.Get(BookingSettings.GoogleBaseUrlKey).Length > 0;

    private string BaseUrl(HazemSettings settings) => (UseStub(settings) ? settings.Get(BookingSettings.GoogleBaseUrlKey) : DefaultBaseUrl).TrimEnd('/');
}
