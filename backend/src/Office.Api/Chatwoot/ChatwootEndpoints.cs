using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Office.Api.Panel;
using Office.Application.Chatwoot;
using Office.Application.Panel;
using Office.Infrastructure.Booking;
using Office.Infrastructure.Chatwoot;

namespace Office.Api.Chatwoot;

/// <summary>
/// TASK 07b (T-24). POST /chatwoot/webhook: what the office's Chatwoot inbox tells the api (on
/// office_net only; the api has no host port), signed with CHATWOOT_WEBHOOK_TOKEN. And the panel's
/// way into Chatwoot without a second password.
/// </summary>
public static class ChatwootEndpoints
{
    /// <summary>How old a signed delivery may be (Chatwoot retries; a copied one is refused later).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public static void MapChatwoot(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chatwoot/webhook", async (HttpContext ctx, ChatwootEvents events, IOptions<ChatwootOptions> options,
            TimeProvider clock, ILoggerFactory loggers, IHostApplicationLifetime lifetime) =>
        {
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ctx.RequestAborted);
            // Chatwoot stops waiting after a few seconds and closes the connection; a reply already
            // claimed must still be sent (or marked failed), so the work runs on the API's lifetime.
            var ct = lifetime.ApplicationStopping;
            if (!Signed(ctx.Request, body, options.Value.WebhookSecret, clock.GetUtcNow()))
            {
                return Results.Unauthorized();
            }

            var log = loggers.CreateLogger("Office.Api.Chatwoot");
            if (JsonNode.Parse(body) is not JsonObject e)
            {
                return Results.BadRequest();
            }

            var kind = (string?)e["event"];
            ChatwootEvents.Result result;
            if (kind == "message_created" && IsStaffMessage(e))
            {
                result = await events.StaffWroteAsync(new ChatwootEvents.StaffMessage(
                    (long)e["id"]!, (long)e["conversation"]!["id"]!, (long?)e["sender"]?["id"], (string?)e["content"],
                    e["attachments"] is JsonArray { Count: > 0 }), ct);
            }
            else if (kind is "conversation_status_changed" or "conversation_resolved" && (string?)e["status"] == "resolved")
            {
                result = await events.ResolvedAsync((long)e["id"]!, ct);
            }
            else
            {
                return Results.Ok();
            }

            // Ids and the outcome only: never the text.
            log.LogInformation("Chatwoot {Event} {Id}: {Result}", kind, (long?)e["id"], result);
            return Results.Ok();
        });

        // The panel opens Chatwoot already signed in (Chatwoot's one-time link), at the client's
        // conversation when it is a task the person may see, or an appointment (for whoever manages the schedule).
        app.MapPost("/api/panel/chatwoot/open", async (OpenRequest? body, HttpContext ctx, ChatwootAccounts accounts,
            PanelTasks tasks, PanelBooking booking, IChatwootStore store, IChatwootApi api, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (!accounts.Configured)
            {
                return Results.Json(new { error = "not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            ChatwootThread? thread = null;
            if (body?.TaskId is { } taskId)
            {
                var task = await tasks.GetAsync(ctx.CurrentUser().AsViewer(), taskId, ct);
                if (task.Outcome != PanelOutcome.Ok)
                {
                    return Results.NotFound(new { error = "not_found" });
                }

                thread = await store.FindThreadAsync(task.Value!.Task.WaId, ct);
            }
            else if (body?.AppointmentId is { } appointmentId)
            {
                var wa = ctx.CurrentUser().AsViewer().Can(Permissions.ScheduleManage) ? await booking.WaIdAsync(appointmentId, ct) : null;
                if (wa is null)
                {
                    return Results.NotFound(new { error = "not_found" });
                }

                thread = await store.FindThreadAsync(wa, ct);
            }

            try
            {
                // After the sign-in link: by then the office's account in Chatwoot is known.
                var login = await accounts.LoginUrlAsync(ctx.CurrentUser(), ct);
                return login is null
                    ? Results.Json(new { error = "not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.Ok(new { login_url = login, conversation_url = thread is null ? null : api.ConversationUrl(thread.ConversationId) });
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ChatwootException)
            {
                loggers.CreateLogger("Office.Api.Chatwoot").LogWarning("Chatwoot sign-in link failed: {Message}", e.Message);
                return Results.Json(new { error = "chatwoot_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AddEndpointFilter<NoStoreFilter>()
        .AddEndpointFilter<AntiforgeryFilter>()
        .AddEndpointFilter<MustChangePasswordFilter>()
        .AddEndpointFilter<TwoFactorRequiredFilter>()
        .RequireAuthorization(PanelAuth.Policy(Permissions.AccountSelf));
    }

    /// <summary>A person's public reply: not Hazem's copy (checked later), not a private note, not the client.</summary>
    private static bool IsStaffMessage(JsonObject e) =>
        (string?)e["message_type"] == "outgoing"
        && e["private"]?.GetValue<bool>() != true
        && ((string?)e["sender"]?["type"])?.Equals("user", StringComparison.OrdinalIgnoreCase) == true
        && e["conversation"]?["id"] is not null;

    /// <summary>X-Chatwoot-Signature = sha256=HMAC(secret, "{X-Chatwoot-Timestamp}.{body}"), and recent.</summary>
    public static bool Signed(HttpRequest request, string body, string secret, DateTimeOffset now)
    {
        if (secret.Length == 0
            || !long.TryParse(request.Headers["X-Chatwoot-Timestamp"].ToString(), out var ts)
            || (now - DateTimeOffset.FromUnixTimeSeconds(ts)).Duration() > MaxAge)
        {
            return false;
        }

        var expected = "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{ts}.{body}")));
        var sent = request.Headers["X-Chatwoot-Signature"].ToString();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(sent), Encoding.ASCII.GetBytes(expected));
    }

    public sealed record OpenRequest(Guid? TaskId, Guid? AppointmentId);
}
