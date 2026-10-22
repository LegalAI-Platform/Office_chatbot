using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Office.Application.Conversations;
using Office.Application.Ports;
using Office.Application.Texts;
using Office.Application.Understanding;
using Office.Infrastructure.Ai;
using Office.Application.Responders;
using Office.Application.UseCases;
using Office.Infrastructure.N8n;
using Office.Infrastructure.Persistence;
using Polly;

namespace Office.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionName = "Office"; // office_api: schema hazem only

    public static IServiceCollection AddOfficeInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is missing.");

        services.AddDbContext<HazemDbContext>(o => o
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddOptions<HazemOptions>().Bind(config.GetSection(HazemOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<HazemOptions>, HazemOptionsValidator>();

        services.AddHttpClient<IWhatsAppGateway, N8nWhatsAppGateway>(N8nWhatsAppGateway.HttpClientName, c =>
            {
                c.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
            })
            .RemoveAllLoggers() // request headers carry the token
            .AddResilienceHandler("n8n", b =>
            {
                b.AddTimeout(TimeSpan.FromSeconds(100));
                // Retrying is safe: n8n never sends the same idempotency key twice.
                b.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(1),
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or TimeoutException
                            or Polly.Timeout.TimeoutRejectedException
                        || args.Outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout),
                });
                b.AddTimeout(TimeSpan.FromSeconds(30));
            });

        services.AddScoped<ITurnStore, TurnStore>();
        services.AddScoped<ISettingsReader, SettingsReader>();
        services.AddScoped<ISendResultStore, SendResultStore>();
        // T-08: the classifier. One call within 15 s and one retry are handled inside it; the
        // client has no loggers because the Authorization header carries the key.
        services.AddHttpClient<IIntentClassifier, OpenAiIntentClassifier>(OpenAiIntentClassifier.HttpClientName,
                c => c.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IConversationStore, ConversationStore>();
        services.AddScoped<IApprovedTexts, ApprovedTextStore>();
        services.AddScoped<ITurnResponder, HazemResponder>();
        services.AddScoped<ITurnResponder, EchoResponder>();
        services.AddScoped<IResponderSelector, ResponderSelector>();
        services.AddScoped<HandleTurn>();
        services.AddScoped<RecordSendResults>();
        services.AddScoped<SendOutbound>();

        return services;
    }
}
