using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Office.Api;
using Office.Api.WhatsApp;
using Office.Infrastructure;
using Office.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning) // request URLs may carry tokens
    .MinimumLevel.Override("Polly", LogEventLevel.Warning)
    .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddOfficeInfrastructure(builder.Configuration);
builder.Services.AddSingleton<WebhookMetrics>();
builder.Services.AddScoped<WhatsAppJobs>();

var appConnection = builder.Configuration.GetConnectionString(DependencyInjection.AppConnectionName)!;
var runHangfireServer = builder.Configuration.GetValue("Hangfire:Server", true);
builder.Services.AddHangfire(h => h
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(appConnection), new PostgreSqlStorageOptions
    {
        SchemaName = DatabaseMigrator.HangfireSchema,
        PrepareSchemaIfNecessary = false, // the schema is installed by `Office.Cli migrate` (office_app has no DDL)
    }));
if (runHangfireServer)
{
    builder.Services.AddHangfireServer(o => o.WorkerCount = 4);
}

builder.Services.AddHealthChecks()
    .AddNpgSql(appConnection, name: "postgres", tags: ["ready"])
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "redis:6379", name: "redis", tags: ["ready"]);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Generous: Meta can burst; this only protects against abuse of the public URL.
    o.AddFixedWindowLimiter(RateLimits.Webhook, l =>
    {
        l.PermitLimit = 600;
        l.Window = TimeSpan.FromMinutes(1);
        l.QueueLimit = 0;
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1; // cloudflared
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(); // users and roles arrive in TASK 07
builder.Services.AddAuthorization();

var app = builder.Build();
Program.Services = app.Services;

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapWhatsAppWebhook();

if (runHangfireServer)
{
    try
    {
        var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
        recurring.AddOrUpdate<WhatsAppJobs>(WhatsAppJobs.TemplatesSyncJobId, j => j.SyncTemplatesAsync(CancellationToken.None), "0 */6 * * *");
        recurring.AddOrUpdate<WhatsAppJobs>(WhatsAppJobs.EchoTestJobId, j => j.EchoTestAsync(CancellationToken.None), Cron.Minutely());
    }
    catch (Exception ex)
    {
        // Usually: migrations not applied yet. Keep serving; /health/ready reports the database state.
        app.Logger.LogError(ex, "Could not register recurring jobs. Run `migrate` and restart the API.");
    }
}

app.Run();

namespace Office.Api
{
    public static class RateLimits
    {
        public const string Webhook = "webhook";
    }
}

public partial class Program
{
    /// <summary>Root provider for Hangfire state filters, which are created outside DI.</summary>
    internal static IServiceProvider? Services { get; set; }
}
