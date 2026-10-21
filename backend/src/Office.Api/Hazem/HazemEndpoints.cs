using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Office.Core.Hazem;
using Office.Infrastructure;
using Office.Infrastructure.N8n;

namespace Office.Api.Hazem;

/// <summary>Endpoints n8n calls (docs/hazem-internal-api.md). Every one needs X-Hazem-Token.</summary>
public static class HazemEndpoints
{
    public const string ReportedByN8n = "n8n";

    public static void MapHazemApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/hazem").AddEndpointFilter<HazemTokenFilter>();

        group.MapPost("/turns", async (TurnRequest? request, TurnService turns, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var errors = TurnRules.Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var response = await turns.HandleAsync(request!, ct);
            // Never log message text, captions, transcripts, file names or the full phone number:
            // only the turn id, the message types and counts.
            loggers.CreateLogger("Office.Api.Hazem.Turns").LogInformation(
                "Turn {TurnId} from {WaId}: {Status}, {MessageCount} message(s) [{MessageTypes}], {ActionCount} action(s)",
                response.TurnId, TurnRules.Mask(request!.WaId), response.Status, request.Messages!.Count,
                string.Join(",", request.Messages.Select(m => m.Type)), response.Actions.Count);
            return Results.Ok(response);
        });

        group.MapPost("/send-results", async (SendResultsRequest? request, ISendResultStore store, CancellationToken ct) =>
        {
            var errors = TurnRules.Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var recorded = await store.UpsertAsync(request!.TurnId, request.Results!, ReportedByN8n, ct);
            return Results.Ok(new { recorded });
        });
    }
}

/// <summary>Constant-time check of X-Hazem-Token against HAZEM_API_TOKEN; 401 otherwise.</summary>
public sealed class HazemTokenFilter(IOptions<HazemOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var sent = context.HttpContext.Request.Headers[N8nWhatsAppGateway.TokenHeader].ToString();
        var expected = options.Value.ApiToken;
        var ok = sent.Length > 0
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(expected));
        return ok ? await next(context) : Results.Unauthorized();
    }
}
