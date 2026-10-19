using Hangfire;
using Microsoft.Extensions.Options;
using Office.Core.WhatsApp;
using Office.Infrastructure.WhatsApp;

namespace Office.Api.WhatsApp;

/// <summary>Meta webhook (T-17): GET verification and signed POST events.</summary>
public static class WhatsAppEndpoints
{
    public const int MaxBodyBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapWhatsAppWebhook(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks/whatsapp")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimits.Webhook)
            .DisableAntiforgery();

        group.MapGet("", Verify);
        group.MapPost("", Receive);
        return app;
    }

    private static IResult Verify(HttpRequest request, IOptions<WhatsAppOptions> options)
    {
        var o = options.Value;
        if (!o.Enabled)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var q = request.Query;
        var token = q["hub.verify_token"].ToString();
        if (q["hub.mode"] == "subscribe" && token.Length > 0 && FixedTimeEquals(token, o.VerifyToken))
        {
            return Results.Text(q["hub.challenge"].ToString(), "text/plain");
        }

        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> Receive(HttpRequest request, IOptions<WhatsAppOptions> options, WebhookIngestion ingestion,
        IBackgroundJobClient jobs, WebhookMetrics metrics, ILoggerFactory loggers, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Enabled)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (request.ContentLength > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        byte[] body;
        using (var buffer = new MemoryStream())
        {
            await request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length > MaxBodyBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            body = buffer.ToArray();
        }

        var result = await ingestion.IngestAsync(body, request.Headers["X-Hub-Signature-256"].ToString(), o.AppSecret, ct);
        var log = loggers.CreateLogger("Office.Api.WhatsApp.Webhook");

        switch (result.Outcome)
        {
            case IngestionOutcome.InvalidSignature:
                log.LogWarning("Rejected webhook POST with a missing or invalid signature ({Bytes} bytes)", body.Length);
                await metrics.SignatureFailedAsync(ct);
                return Results.Unauthorized();
            case IngestionOutcome.InvalidJson:
                log.LogWarning("Rejected webhook POST with invalid JSON");
                return Results.BadRequest();
            case IngestionOutcome.NotWhatsApp:
                return Results.Ok();
        }

        foreach (var id in result.NewEventIds)
        {
            jobs.Enqueue<WhatsAppJobs>(j => j.ProcessEventAsync(id, CancellationToken.None));
        }

        log.LogInformation("Webhook accepted: {NewEvents} new event(s)", result.NewEventIds.Count);
        return Results.Ok();
    }

    private static bool FixedTimeEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}

/// <summary>Counts signature failures and alerts the operator when they spike (T-16).</summary>
public sealed class WebhookMetrics(IOpsNotifier notifier, TimeProvider clock)
{
    public const int AlertThreshold = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private readonly Queue<DateTimeOffset> _failures = new();
    private readonly Lock _gate = new();
    private DateTimeOffset _lastAlert = DateTimeOffset.MinValue;

    public Task SignatureFailedAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        bool alert;
        lock (_gate)
        {
            _failures.Enqueue(now);
            while (_failures.Count > 0 && now - _failures.Peek() > Window)
            {
                _failures.Dequeue();
            }

            alert = _failures.Count >= AlertThreshold && now - _lastAlert > Window;
            if (alert)
            {
                _lastAlert = now;
            }
        }

        return alert
            ? notifier.NotifyAsync($"{AlertThreshold}+ webhook requests with invalid signatures in 5 minutes.", ct)
            : Task.CompletedTask;
    }
}
