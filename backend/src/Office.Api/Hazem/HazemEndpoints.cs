using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Office.Api.Contracts;
using Office.Application.UseCases;
using Office.Infrastructure;
using Office.Infrastructure.N8n;

namespace Office.Api.Hazem;

/// <summary>Endpoints n8n calls (docs/hazem-internal-api.md). Every one needs X-Hazem-Token.</summary>
public static class HazemEndpoints
{
    public static void MapHazemApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/hazem").AddEndpointFilter<HazemTokenFilter>();

        group.MapPost("/turns", async (TurnRequest? request, HandleTurn turns, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var errors = TurnRules.Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var result = await turns.HandleAsync(request!.ToTurn(), ct);
            var response = result.ToResponse();
            // Never log message text, captions, transcripts, file names or the full phone number:
            // only the turn id, the message types and counts, and why a text was not sent (keys and
            // reasons only — Notes never hold a text).
            var log = loggers.CreateLogger("Office.Api.Hazem.Turns");
            log.LogInformation(
                "Turn {TurnId} from {WaId}: {Status}, {MessageCount} message(s) [{MessageTypes}], {ActionCount} action(s)",
                response.TurnId, TurnRules.Mask(request!.WaId), response.Status, request.Messages!.Count,
                string.Join(",", request.Messages.Select(m => m!.Type)), response.Actions.Count);
            if (result.Notes is { Count: > 0 } notes)
            {
                log.LogWarning("Turn {TurnId}: {Notes}", response.TurnId, string.Join("; ", notes));
            }

            return Results.Ok(response);
        });

        // B-12 from the panel (TASK 07) or n8n (TASK 05b): a staff member wrote, or handed back.
        group.MapPost("/staff-events", async (StaffEventRequest? request, StaffEvents events, CancellationToken ct) =>
        {
            if (request is null || !TurnRules.IsValidWaId(request.WaId)
                || request.Event is not (StaffEvents.StaffReplied or StaffEvents.HandBack))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["body"] = ["wa_id (8-15 digits) and event (staff_replied | hand_back) are required"],
                });
            }

            var (outcome, state, error) = await events.ApplyAsync(request.WaId!, request.Event, request.At ?? DateTimeOffset.UtcNow, ct);
            return outcome switch
            {
                StaffEvents.Outcome.UnknownNumber => Results.NotFound(),
                StaffEvents.Outcome.Refused => Results.Conflict(new { state, error }),
                _ => Results.Ok(new { state }),
            };
        });

        group.MapPost("/send-results", async (SendResultsRequest? request, RecordSendResults results, CancellationToken ct) =>
        {
            var errors = TurnRules.Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var recorded = await results.RecordAsync(request!.TurnId, request.Results!, ct);
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
