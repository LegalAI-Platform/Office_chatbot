using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Office.Core.WhatsApp;
using Office.Infrastructure.WhatsApp;

namespace Office.IntegrationTests;

/// <summary>The real API, pointed at the throwaway database, with Meta and Telegram faked at the HTTP boundary.</summary>
public sealed class ApiFactory(TestDatabase db) : WebApplicationFactory<Program>
{
    public const string AppSecret = "integration-test-app-secret";
    public const string VerifyToken = "integration-test-verify-token";
    public const string PhoneNumberId = "100000000000001";
    public const string TestRecipient = "201000000001";

    public FakeGraphHandler Graph { get; } = new();
    public FakeNotifier Notifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("test");
        builder.UseSetting("ConnectionStrings:Office", db.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", db.RedisConnectionString);
        builder.UseSetting("Hangfire:Server", "false"); // tests run the jobs explicitly
        builder.UseSetting("WhatsApp:Enabled", "true");
        builder.UseSetting("WhatsApp:AppId", "900000000000001");
        builder.UseSetting("WhatsApp:WabaId", "800000000000001");
        builder.UseSetting("WhatsApp:PhoneNumberId", PhoneNumberId);
        builder.UseSetting("WhatsApp:GraphApiVersion", "v26.0");
        builder.UseSetting("WhatsApp:AppSecret", AppSecret);
        builder.UseSetting("WhatsApp:AccessToken", "integration-test-token");
        builder.UseSetting("WhatsApp:VerifyToken", VerifyToken);
        builder.UseSetting("WhatsApp:TestRecipients", TestRecipient);

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(GraphApiClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Graph);
            services.AddSingleton<IOpsNotifier>(Notifier);
        });
    }
}

/// <summary>Records every Graph API request and answers with a configurable response.</summary>
public sealed class FakeGraphHandler : HttpMessageHandler
{
    private int _counter;
    public ConcurrentQueue<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();
    public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

    public void Reset()
    {
        Requests.Clear();
        Responder = null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Enqueue((request.Method, request.RequestUri!.AbsolutePath, body));
        if (Responder is not null)
        {
            return Responder(request);
        }

        var wamid = $"wamid.FAKE-OUT-{Interlocked.Increment(ref _counter)}";
        return Json(HttpStatusCode.OK, new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["messages"] = new JsonArray(new JsonObject { ["id"] = wamid }),
        });
    }

    public static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage MetaError(int code, string message) =>
        Json(HttpStatusCode.BadRequest, new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } });
}

public sealed class FakeNotifier : IOpsNotifier
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public Task NotifyAsync(string message, CancellationToken ct)
    {
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }
}
