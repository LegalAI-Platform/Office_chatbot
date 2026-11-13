using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Office.Api;
using Office.Api.Chatwoot;
using Office.Api.Hazem;
using Office.Api.Panel;
using Office.Infrastructure.Json;
using Office.Infrastructure;
using Office.Infrastructure.Persistence;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// The brain (DECISIONS v1.3): n8n hands it every turn and sends whatever it answers.
// It never talks to Meta and has no access to the WhatsApp tables.
var builder = WebApplication.CreateBuilder(args);

// Serilog writes the JSON console log; other providers (only tests add one, to prove that no
// message text reaches the logs) also receive every event.
builder.Logging.ClearProviders();
builder.Host.UseSerilog(writeToProviders: true, configureLogger: (ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .MinimumLevel.Override("Polly", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 1024 * 1024; // a turn is far smaller
    k.AddServerHeader = false; // no "Server: Kestrel" telling a visitor what runs here
});
builder.Services.ConfigureHttpJsonOptions(o => HazemJson.Configure(o.SerializerOptions));
builder.Services.AddOfficeInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck<HazemSchemaHealthCheck>("hazem-schema", tags: ["ready"]);
builder.Services.AddPanelAuth(builder.Configuration, builder.Environment); // TASK 07a: the panel (/api/panel)
builder.Services.AddHostedService<Office.Infrastructure.Chatwoot.ChatwootWorker>(); // TASK 07b: turns into Chatwoot
builder.Services.AddHostedService<Office.Infrastructure.Panel.AlertWorker>(); // TASK 07c: overdue tasks and alerts
builder.Services.AddHostedService<Office.Infrastructure.Booking.CalendarWorker>(); // TASK 08: the lawyer's Google Calendar

// v1.107 (the monitoring, docs/runbooks/monitoring.md): the API's numbers for Prometheus. The requests, the .NET runtime,
// Npgsql's connection pool, the calls it makes (OpenAI, Anthropic, Chatwoot, n8n, Google) and the alerts' rounds. Never a
// client's data: a request is known by its route's template (no address, no query), and no label comes from what was sent.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("office-api"))
    .WithMetrics(m => m
        .AddMeter(OfficeMetrics.Meters)
        .AddPrometheusExporter());

var app = builder.Build();

// v1.107: /metrics on its own port only (Metrics:Port, 9464; the image listens on 8080 and 9464). Only the Docker networks reach
// it: nginx sends /api/panel alone to the API, the edge never reaches the API, and 9464 has no host port. Anything else on that
// port answers 404, and /metrics on 8080 is 404 like any unknown path. The scrapes themselves are not counted as requests.
var metricsPort = app.Configuration.GetValue("Metrics:Port", OfficeMetrics.DefaultPort);
app.Use(async (ctx, next) =>
{
    if (ctx.Connection.LocalPort != metricsPort)
    {
        await next();
        return;
    }

    if (ctx.Features.Get<IHttpMetricsTagsFeature>() is { } tags)
    {
        tags.MetricsDisabled = true;
    }

    if (!HttpMethods.IsGet(ctx.Request.Method) || ctx.Request.Path != "/metrics")
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});
app.UseOpenTelemetryPrometheusScrapingEndpoint(ctx => ctx.Connection.LocalPort == metricsPort && ctx.Request.Path == "/metrics");

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Docker and the monitoring ask every few seconds: not counted, so the numbers are the office's own traffic (v1.107).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).DisableHttpMetrics();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).DisableHttpMetrics();
app.MapHazemApi();
app.MapPanelApi();
app.MapChatwoot();
app.MapPanelTools(); // TASK 07c
app.MapPanelTesting(); // «أدوات التجربة», test environment only
app.MapPanelBooking(); // TASK 08
app.MapPanelServices(); // TASK 10
app.MapPanelUrf(); // TASK 11
app.MapPanelPrices(); // TASK 06

app.Run();

/// <summary>Ready = the database answers as office_api and migration 0003 (schema hazem) is applied.</summary>
internal sealed class HazemSchemaHealthCheck(HazemDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await db.Settings.AsNoTracking().AnyAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("database or schema hazem not ready (run migrate up)");
        }
    }
}

public partial class Program;
