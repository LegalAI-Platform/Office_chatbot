using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Office.Infrastructure.Ai;
using Office.Infrastructure.N8n;

namespace Office.IntegrationTests;

/// <summary>The real API on the throwaway database (as office_api), with n8n faked at the HTTP boundary.</summary>
public sealed class ApiFactory(TestDatabase db) : WebApplicationFactory<Program>
{
    public const string ApiToken = "integration-test-api-token-0123456789abcdef";
    public const string N8nToken = "integration-test-n8n-token-0123456789abcdef";
    public const string N8nUrl = "http://n8n.test/webhook/hazem-internal-send";

    public FakeN8n N8n { get; } = new();
    public FakeOpenAi OpenAi { get; } = new();
    public const string OpenAiKey = "sk-integration-test-not-a-real-key";
    public MemoryLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("test");
        builder.UseSetting("ConnectionStrings:Office", db.ApiConnectionString);
        builder.UseSetting("Hazem:ApiToken", ApiToken);
        builder.UseSetting("Hazem:N8nInternalSendUrl", N8nUrl);
        builder.UseSetting("Hazem:N8nInternalSendToken", N8nToken);
        builder.UseSetting("Hazem:OpenAiApiKey", OpenAiKey);
        builder.ConfigureLogging(l => l.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(N8nWhatsAppGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => N8n);
            services.AddHttpClient(OpenAiIntentClassifier.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => OpenAi);
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

/// <summary>Keeps every formatted log line written by the API, so a test can read them.</summary>
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
