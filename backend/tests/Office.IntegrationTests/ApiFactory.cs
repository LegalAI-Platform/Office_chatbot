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
        Mail.Configured = SignupConfigured;
        builder.ConfigureLogging(l => l.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(N8nWhatsAppGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => N8n);
            services.AddHttpClient(OpenAiIntentClassifier.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => OpenAi);
            services.AddSingleton<IPanelMailer>(Mail);
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
