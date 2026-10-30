using Office.Application.Panel;
using Office.Infrastructure.Panel;

namespace Office.Api.Panel;

/// <summary>
/// «أدوات التجربة» (the owner, 2026-10-30): Hazem forgets one of his test numbers from the panel (PanelTesting).
/// The manager and the lawyer only (testing.forget), the same filters as the rest of /api/panel, and in the test
/// environment only: anywhere else every path here answers 404, as if it did not exist.
/// </summary>
public static class PanelTestingEndpoints
{
    public static void MapPanelTesting(this IEndpointRouteBuilder app)
    {
        var testing = app.MapGroup("/api/panel/testing")
            .AddEndpointFilter(async (context, next) =>
                context.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsEnvironment("test")
                    ? await next(context)
                    : Results.NotFound(new { error = "not_found" }))
            .AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<AntiforgeryFilter>()
            .AddEndpointFilter<MustChangePasswordFilter>()
            .AddEndpointFilter<TwoFactorRequiredFilter>()
            .RequireAuthorization(PanelAuth.Policy(Permissions.TestingForget));

        testing.MapGet("/numbers", async (PanelTesting t, CancellationToken ct) =>
            Results.Ok(new { numbers = (await t.NumbersAsync(ct)).Select(n => new { key = n.Key, masked = n.Masked }) }));

        testing.MapPost("/forget", async (ForgetRequest? body, HttpContext ctx, PanelTesting t, CancellationToken ct) =>
        {
            if (body?.Key is not { Length: 16 } key)
            {
                return Results.BadRequest(new { error = "invalid" });
            }

            var r = await t.ForgetAsync(ctx.CurrentUser().Id, key, ct);
            return r is null
                ? Results.NotFound(new { error = "not_a_test_number" })
                : Results.Ok(new
                {
                    masked = r.Masked,
                    rows = r.Rows,
                    calendar_deleted = r.CalendarDeleted,
                    calendar_by_hand = r.CalendarByHand,
                    chatwoot = r.Chatwoot,
                    chatwoot_conversations = r.ChatwootConversations,
                });
        });
    }

    private sealed record ForgetRequest(string? Key);
}
