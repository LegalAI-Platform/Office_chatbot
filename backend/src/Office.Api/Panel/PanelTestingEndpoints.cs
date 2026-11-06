using Office.Application.Panel;
using Office.Infrastructure.Panel;

namespace Office.Api.Panel;

/// <summary>
/// «أدوات التجربة» (the owner, 2026-10-30): Hazem forgets one of his test numbers from the panel (PanelTesting), and
/// (v1.34, B-09.6) the switch for the urgent alert inside the 24-hour window.
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

        // v1.34 (B-09.6): the urgent alert as a normal message inside the 24-hour window, until Meta approves urgent_alert.
        testing.MapGet("/urgent-window", async (PanelTesting t, CancellationToken ct) => Results.Ok(new { on = await t.UrgentWindowAsync(ct) }));

        testing.MapPut("/urgent-window", async (UrgentWindowRequest? body, HttpContext ctx, PanelTesting t, CancellationToken ct) =>
        {
            if (body?.On is not { } on)
            {
                return Results.BadRequest(new { error = "invalid" });
            }

            var me = ctx.CurrentUser();
            await t.SetUrgentWindowAsync(me.Id, me.Username, on, ct);
            return Results.Ok(new { on });
        });

        // v1.52 (phase 1): Hazem reads the whole chat and the client's stance; off is Hazem exactly as before.
        testing.MapGet("/understanding", async (PanelTesting t, CancellationToken ct) => Results.Ok(new { on = await t.UnderstandingAsync(ct) }));

        testing.MapPut("/understanding", async (UrgentWindowRequest? body, HttpContext ctx, PanelTesting t, CancellationToken ct) =>
        {
            if (body?.On is not { } on)
            {
                return Results.BadRequest(new { error = "invalid" });
            }

            var me = ctx.CurrentUser();
            await t.SetUnderstandingAsync(me.Id, me.Username, on, ct);
            return Results.Ok(new { on });
        });

        // v1.61: a sentence that answers his question before the office's address and hours; off is the approved text alone.
        testing.MapGet("/info-writer", async (PanelTesting t, CancellationToken ct) => Results.Ok(new { on = await t.InfoWriterAsync(ct) }));

        testing.MapPut("/info-writer", async (UrgentWindowRequest? body, HttpContext ctx, PanelTesting t, CancellationToken ct) =>
        {
            if (body?.On is not { } on)
            {
                return Results.BadRequest(new { error = "invalid" });
            }

            var me = ctx.CurrentUser();
            await t.SetInfoWriterAsync(me.Id, me.Username, on, ct);
            return Results.Ok(new { on });
        });

        // v1.84 (TASK 20): the Urfi case as one list (said / asked / taken back); off is the Urfi case exactly as v1.81.
        testing.MapGet("/urf-case-said", async (PanelTesting t, CancellationToken ct) => Results.Ok(new { on = await t.CaseSaidAsync(ct) }));

        testing.MapPut("/urf-case-said", async (UrgentWindowRequest? body, HttpContext ctx, PanelTesting t, CancellationToken ct) =>
        {
            if (body?.On is not { } on)
            {
                return Results.BadRequest(new { error = "invalid" });
            }

            var me = ctx.CurrentUser();
            await t.SetCaseSaidAsync(me.Id, me.Username, on, ct);
            return Results.Ok(new { on });
        });

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

    private sealed record UrgentWindowRequest(bool? On);
}
