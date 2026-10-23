using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Office.Api.Chatwoot;
using Office.Api.Hazem;
using Office.Api.Panel;
using Office.Infrastructure.Json;
using Office.Infrastructure;
using Office.Infrastructure.Persistence;
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

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1024 * 1024); // a turn is far smaller
builder.Services.ConfigureHttpJsonOptions(o => HazemJson.Configure(o.SerializerOptions));
builder.Services.AddOfficeInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck<HazemSchemaHealthCheck>("hazem-schema", tags: ["ready"]);
builder.Services.AddPanelAuth(builder.Configuration, builder.Environment); // TASK 07a: the panel (/api/panel)
builder.Services.AddHostedService<Office.Infrastructure.Chatwoot.ChatwootWorker>(); // TASK 07b: turns into Chatwoot

var app = builder.Build();

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

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapHazemApi();
app.MapPanelApi();
app.MapChatwoot();

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
