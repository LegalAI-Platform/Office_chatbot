using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Office.Core.WhatsApp;
using Office.Infrastructure.Ops;
using Office.Infrastructure.Persistence;
using Office.Infrastructure.WhatsApp;
using Polly;

namespace Office.Infrastructure;

public static class DependencyInjection
{
    public const string AppConnectionName = "Office";          // office_app (DML only)
    public const string MigratorConnectionName = "OfficeMigrator"; // office_migrator (DDL), CLI migrate only

    public static IServiceCollection AddOfficeInfrastructure(this IServiceCollection services, IConfiguration config, string connectionName = AppConnectionName)
    {
        var connectionString = config.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{connectionName} is missing.");

        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<OfficeDbContext>(o => o
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable(OfficeDbContext.MigrationsHistoryTable, OfficeDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddOptions<WhatsAppOptions>().Bind(config.GetSection(WhatsAppOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<WhatsAppOptions>, WhatsAppOptionsValidator>();
        services.AddOptions<TelegramOptions>().Bind(config.GetSection(TelegramOptions.Section));

        services.AddHttpClient<GraphApiClient>(GraphApiClient.HttpClientName, c =>
            {
                c.BaseAddress = new Uri("https://graph.facebook.com/");
                c.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
            })
            .RemoveAllLoggers() // request URLs must never reach the logs
            .AddResilienceHandler("graph", b =>
            {
                b.AddTimeout(TimeSpan.FromSeconds(45));
                b.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(1),
                    // Only failures where Meta certainly did not process the request:
                    // no timeouts and no plain 500, so a message is never sent twice.
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException
                        || args.Outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests
                            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout),
                });
                b.AddTimeout(TimeSpan.FromSeconds(15));
            });

        services.AddHttpClient<IOpsNotifier, TelegramNotifier>(TelegramNotifier.HttpClientName, c =>
            {
                c.BaseAddress = new Uri("https://api.telegram.org/");
                c.Timeout = TimeSpan.FromSeconds(10);
            })
            .RemoveAllLoggers(); // the bot token is part of the URL

        services.AddScoped<IWhatsAppStore, WhatsAppStore>();
        services.AddScoped<IWhatsAppCloudApi, WhatsAppCloudApi>();
        services.AddScoped<IWhatsAppSettings, WhatsAppSettings>();
        services.AddScoped<MetaAdmin>();
        services.AddScoped<WebhookIngestion>();
        services.AddScoped<EventProcessor>();
        services.AddScoped<MessageSender>();
        services.AddScoped<EchoResponder>();

        return services;
    }
}
