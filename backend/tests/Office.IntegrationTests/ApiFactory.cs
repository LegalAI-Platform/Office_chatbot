using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Office.Application.Panel;
using Office.Infrastructure.Ai;
using Office.Infrastructure.N8n;

namespace Office.IntegrationTests;

/// <summary>
/// The real API on the throwaway database (as office_api), with n8n faked at the HTTP boundary.
/// <paramref name="clock"/>: a fixed time for rules that depend on the hour (B-06), so a test does
/// not pass or fail with the time it happens to run at.
/// </summary>
public sealed class ApiFactory(TestDatabase db, TimeProvider? clock = null) : WebApplicationFactory<Program>
{
    public const string ApiToken = "integration-test-api-token-0123456789abcdef";
    public const string N8nToken = "integration-test-n8n-token-0123456789abcdef";
    public const string N8nUrl = "http://n8n.test/webhook/hazem-internal-send";

    public FakeN8n N8n { get; } = new();
    public FakeOpenAi OpenAi { get; } = new();
    public const string OpenAiKey = "sk-integration-test-not-a-real-key";
    public MemoryLogs Logs { get; } = new();
    public FakeMailer Mail { get; } = new();
    public const string PanelSecretKey = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    /// <summary>False: PANEL_SECRET_KEY and the mail server are not set (sign-up and 2FA unavailable).</summary>
    public bool SignupConfigured { get; init; } = true;

    /// <summary>TASK 07b: Chatwoot faked at the HTTP boundary; the link is on only when a test asks.</summary>
    public FakeChatwoot Chatwoot { get; } = new();
    public bool ChatwootConfigured { get; init; }
    public const string ChatwootWebhookSecret = "integration-test-chatwoot-webhook-secret-0123456789";

    /// <summary>The v1.9 rule that a manager must turn the authenticator on; off unless a test is about it.</summary>
    public bool ManagersNeedTwoFactor { get; init; }

    /// <summary>The panel's login rate limit; high by default because tests sign in many times a minute.</summary>
    public int LoginsPerMinute { get; init; } = 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("test");
        builder.UseSetting("ConnectionStrings:Office", db.ApiConnectionString);
        builder.UseSetting("Hazem:ApiToken", ApiToken);
        builder.UseSetting("Hazem:N8nInternalSendUrl", N8nUrl);
        builder.UseSetting("Hazem:N8nInternalSendToken", N8nToken);
        builder.UseSetting("Hazem:OpenAiApiKey", OpenAiKey);
        builder.UseSetting("Panel:LoginsPerMinute", LoginsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Panel:SecretKey", SignupConfigured ? PanelSecretKey : "");
        builder.UseSetting("Panel:ManagersNeedTwoFactor", ManagersNeedTwoFactor ? "true" : "false");
        builder.UseSetting("Chatwoot:RunWorker", "false");
        builder.UseSetting("Chatwoot:BaseUrl", "http://chatwoot.test");
        builder.UseSetting("Chatwoot:PublicUrl", "http://127.0.0.1:3000");
        builder.UseSetting("Chatwoot:BotToken", ChatwootConfigured ? "integration-test-chatwoot-bot-token" : "");
        builder.UseSetting("Chatwoot:PlatformToken", ChatwootConfigured ? "integration-test-chatwoot-platform-token" : "");
        builder.UseSetting("Chatwoot:WebhookSecret", ChatwootWebhookSecret);
        Mail.Configured = SignupConfigured;
        builder.ConfigureLogging(l => l.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(N8nWhatsAppGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => N8n);
            services.AddHttpClient(OpenAiIntentClassifier.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => OpenAi);
            services.AddSingleton<IPanelMailer>(Mail);
            services.AddHttpClient(Office.Infrastructure.Chatwoot.ChatwootApi.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Chatwoot);
            if (clock is not null)
            {
                services.AddSingleton(clock);
            }
        });
    }

    public HttpClient CreateN8nClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(N8nWhatsAppGateway.TokenHeader, ApiToken);
        return client;
    }
}

/// <summary>
/// OpenAI at the HTTP boundary: answers Structured Outputs by keywords (like the e2e stub) and
/// records what it received, so a test can prove what the AI saw (T-09). Never a real call.
/// </summary>
public sealed class FakeOpenAi : HttpMessageHandler
{
    public ConcurrentQueue<(string? Auth, string UserText)> Requests { get; } = new();
    public bool Down { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
        var text = (string?)body["messages"]?[1]?["content"] ?? "";
        Requests.Enqueue((request.Headers.Authorization?.ToString(), text));
        if (Down)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var (intent, service, name) =
            text.Contains("أحجز", StringComparison.Ordinal) ? ("consultation_request", null, null)
            : text.Contains("أطلق", StringComparison.Ordinal) ? ("legal_question", null, null)
            : text.Contains("موظف", StringComparison.Ordinal) ? ("human_request", null, null)
            : text.Contains("السلام", StringComparison.Ordinal) ? ("greeting", null, null)
            : text.Contains("شكرا", StringComparison.Ordinal) ? ("thanks_or_closing", null, null)
            : text.Contains("الجلسة", StringComparison.Ordinal) ? ("existing_matter", null, null)
            : text.Split(' ').Length >= 4 ? ("provide_name", (string?)null, (string?)text)
            : ("other", null, null);
        var content = new JsonObject
        {
            ["intent"] = intent, ["service"] = service, ["consultation_mode"] = "unknown", ["full_name"] = name, ["confidence"] = 0.9,
        }.ToJsonString();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = content } }),
                ["usage"] = new JsonObject { ["prompt_tokens"] = 400, ["completion_tokens"] = 20 },
            }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Records every call to n8n's internal webhook and answers like WA-INTERNAL-SEND.</summary>
public sealed class FakeN8n : HttpMessageHandler
{
    private int _counter;
    public ConcurrentQueue<(string Url, string? Token, string Body)> Requests { get; } = new();
    public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var token = request.Headers.TryGetValues(N8nWhatsAppGateway.TokenHeader, out var v) ? v.Single() : null;
        Requests.Enqueue((request.RequestUri!.ToString(), token, body));
        if (Responder is not null)
        {
            return Responder(request);
        }

        var n = Interlocked.Increment(ref _counter);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject
            {
                ["ok"] = true,
                ["outbound_id"] = $"00000000-0000-0000-0000-{n:D12}",
                ["wamid"] = $"wamid.FAKE-{n}",
                ["status"] = "sent",
            }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>A clock stopped at one instant.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}

/// <summary>Keeps every formatted log line written by the API, so a test can read them.</summary>
/// <summary>
/// Chatwoot at the HTTP boundary (TASK 07b), shaped like the real v4.18 answers: the account and
/// inbox by name, contacts, conversations, messages, and the platform API's users. Records every
/// call (path, token, body) so a test can prove what Chatwoot was told.
/// </summary>
public sealed class FakeChatwoot : HttpMessageHandler
{
    public const long Account = 2, Inbox = 1, SystemUser = 2;
    public const string SystemEmail = "hazem@office.invalid";
    private long _next = 100;
    public ConcurrentQueue<(string Method, string Path, string? Token, JsonNode? Body)> Calls { get; } = new();
    public bool Down { get; set; }

    /// <summary>Fails the n-th message post from now on (a turn copied half-way), then works again.</summary>
    public int FailMessagePostAt { get; private set; } = -1;
    private int _messagePosts;

    public void ArmMessageFailure(int nth)
    {
        _messagePosts = 0;
        FailMessagePostAt = nth;
    }

    public IReadOnlyList<JsonNode> Messages(long conversation) => Calls
        .Where(c => c.Method == "POST" && c.Path == $"/api/v1/accounts/{Account}/conversations/{conversation}/messages")
        .Select(c => c.Body!).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct));
        var path = request.RequestUri!.AbsolutePath;
        var token = request.Headers.TryGetValues("api_access_token", out var v) ? v.Single() : null;
        // A message post that fails is not stored by Chatwoot, so it is not recorded either.
        if (request.Method == HttpMethod.Post && path.EndsWith("/messages", StringComparison.Ordinal)
            && Interlocked.Increment(ref _messagePosts) - 1 == FailMessagePostAt)
        {
            FailMessagePostAt = -1;
            return new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
        }

        Calls.Enqueue((request.Method.Method, path, token, body));
        if (Down)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var a = $"/api/v1/accounts/{Account}";
        JsonNode answer = (request.Method.Method, path) switch
        {
            ("GET", "/api/v1/profile") => new JsonObject
            {
                ["id"] = SystemUser,
                ["accounts"] = new JsonArray(new JsonObject { ["id"] = Account, ["name"] = "مكتب سعد فتحي سعد" }),
            },
            ("GET", var p) when p == $"{a}/inboxes" => new JsonObject
            {
                ["payload"] = new JsonArray(new JsonObject { ["id"] = Inbox, ["name"] = "واتساب (حازم)" }),
            },
            ("GET", var p) when p == $"{a}/contacts/search" => new JsonObject { ["payload"] = new JsonArray() },
            ("POST", var p) when p == $"{a}/contacts" => new JsonObject
            {
                ["payload"] = new JsonObject
                {
                    ["contact"] = new JsonObject
                    {
                        ["id"] = Interlocked.Increment(ref _next),
                        ["contact_inboxes"] = new JsonArray(new JsonObject { ["source_id"] = Guid.NewGuid().ToString() }),
                    },
                },
            },
            ("POST", var p) when p == $"{a}/conversations" => new JsonObject { ["id"] = Interlocked.Increment(ref _next) },
            ("POST", var p) when p.StartsWith($"{a}/conversations/", StringComparison.Ordinal) =>
                new JsonObject { ["id"] = Interlocked.Increment(ref _next) },
            // Chatwoot hands back the user that already has this e-mail (here: its system user's).
            ("POST", "/platform/api/v1/users") => new JsonObject
            {
                ["id"] = (string?)body?["email"] == SystemEmail ? SystemUser : Interlocked.Increment(ref _next),
            },
            (_, var p) when p == $"/platform/api/v1/accounts/{Account}/account_users" => new JsonObject(),
            ("POST", var p) when p == $"{a}/inbox_members" => new JsonObject { ["payload"] = new JsonArray() },
            ("GET", var p) when p.StartsWith("/platform/api/v1/users/", StringComparison.Ordinal) && p.EndsWith("/login", StringComparison.Ordinal) =>
                new JsonObject { ["url"] = "http://chatwoot:3000/app/login?email=x%40example.test&sso_auth_token=one-time" },
            _ => new JsonObject { ["error"] = "unknown fake path" },
        };
        return new HttpResponseMessage(answer["error"] is null ? HttpStatusCode.OK : HttpStatusCode.NotFound)
        {
            Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>The no-reply mail server: keeps every mail so a test can read the code a person would get.</summary>
public sealed class FakeMailer : IPanelMailer
{
    public ConcurrentQueue<PanelMail> Sent { get; } = new();
    public bool Configured { get; set; } = true;

    public Task<bool> SendAsync(PanelMail mail, CancellationToken ct)
    {
        Sent.Enqueue(mail);
        return Task.FromResult(true);
    }

    public IReadOnlyList<PanelMail> To(string address) =>
        Sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The six digits in the newest mail to this address.</summary>
    public string LastCode(string address) =>
        System.Text.RegularExpressions.Regex.Match(To(address)[^1].Text, @"(?m)^\d{6}$").Value;
}

public sealed class MemoryLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(MemoryLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Lines.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}
