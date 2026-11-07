using System.Text.RegularExpressions;
using Office.Application.Panel;
using Office.Application.UseCases;
using Office.Infrastructure.Lists;
using Office.Infrastructure.Panel;

namespace Office.Api.Panel;

/// <summary>
/// TASK 07c (DECISIONS v1.11): the office's lists, muted conversations, Hazem's approved texts and
/// the month's reports. Each names its permission (RBAC, T-23); the same filters as the rest of
/// /api/panel (antiforgery, a temporary password first, a manager's 2FA first, never cached).
/// </summary>
public static partial class PanelToolsEndpoints
{
    [GeneratedRegex("^[a-z][a-z0-9_]{1,63}$")]
    private static partial Regex TextKey();

    public static void MapPanelTools(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/panel")
            .AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<AntiforgeryFilter>()
            .AddEndpointFilter<MustChangePasswordFilter>()
            .AddEndpointFilter<TwoFactorRequiredFilter>();

        // ---------- lists (B-14) ----------
        var lists = panel.MapGroup("/lists").RequireAuthorization(PanelAuth.Policy(Permissions.ListsManage));

        lists.MapGet("", async (string? kind, string? q, int? page, PanelTools tools, CancellationToken ct) =>
        {
            var (items, total) = await tools.ListsAsync(kind, q is { Length: > 0 and <= 60 } ? q : null, Math.Clamp(page ?? 1, 1, 10_000), ct);
            return Results.Ok(new
            {
                total,
                items = items.Select(e => new ListEntryDto(e.Id, e.Kind, e.WaId, e.Name, e.StaffName, e.Note, e.ImportedAt)),
            });
        });

        lists.MapPost("", async (ListEntryRequest? body, HttpContext ctx, PanelTools tools, CancellationToken ct) =>
        {
            var error = await tools.AddToListAsync(ctx.CurrentUser().Id, body?.Kind, body?.Phone, body?.Name, body?.StaffName, body?.Note, ct);
            return error is null ? Results.NoContent() : Results.BadRequest(new { error });
        });

        lists.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, PanelTools tools, CancellationToken ct) =>
            await tools.RemoveFromListAsync(ctx.CurrentUser().Id, id, ct) ? Results.NoContent() : Results.NotFound(new { error = "not_found" }));

        // A CSV of name,phone[,staff] (the same as the import-lists command). Replace swaps the
        // whole list only when every line is valid.
        lists.MapPost("/import", async (ListImportRequest? body, HttpContext ctx, PanelTools tools, CancellationToken ct) =>
        {
            // nginx and Kestrel let 1 MB through; 300,000 characters of Arabic (2 bytes each) fit, about
            // 6,000 lines. The panel says so before sending a bigger file.
            if (body?.Kind is null || !ListImporter.Kinds.Contains(body.Kind) || body.Csv is not { Length: > 0 and <= 300_000 })
            {
                return Results.BadRequest(new { error = "invalid" });
            }

            var r = await tools.ImportAsync(ctx.CurrentUser().Id, body.Kind, body.Csv, body.Replace, ct);
            return Results.Ok(new { imported = r.Imported, rejected = r.Rejected.Select(x => new { line = x.Line, reason = x.Reason }) });
        });

        // ---------- muted conversations (S-01: the manager's decision only) ----------
        var muted = panel.MapGroup("/conversations").RequireAuthorization(PanelAuth.Policy(Permissions.ConversationsUnmute));

        muted.MapGet("/muted", async (PanelTools tools, CancellationToken ct) =>
            Results.Ok((await tools.MutedAsync(ct)).Select(m => new MutedDto(m.WaId, m.Name, m.Since))));

        // The number goes in the body, never in the address (an address ends up in access logs).
        muted.MapPost("/unmute", async (UnmuteRequest? body, HttpContext ctx, StaffEvents events, PanelTools tools, CancellationToken ct) =>
        {
            if (!Office.Domain.Common.PhoneNumbers.TryToWaId(body?.WaId ?? "", out var wa))
            {
                return Results.BadRequest(new { error = "invalid_phone" });
            }

            var (outcome, error) = await events.UnmuteAsync(wa, ct);
            if (outcome != StaffEvents.Outcome.Done)
            {
                return outcome == StaffEvents.Outcome.UnknownNumber ? Results.NotFound(new { error = "not_found" }) : Results.Conflict(new { error });
            }

            await tools.AuditUnmuteAsync(ctx.CurrentUser().Id, wa, ct);
            return Results.NoContent();
        });

        // ---------- approved texts (the manager and the lawyer; every version kept) ----------
        var texts = panel.MapGroup("/texts").RequireAuthorization(PanelAuth.Policy(Permissions.TextsManage));

        texts.MapGet("", async (PanelTools tools, CancellationToken ct) =>
            Results.Ok((await tools.TextsAsync(ct)).Select(t => new TextDto(t.Key, t.Version, t.Text, t.MaxChars, t.Source, t.UpdatedAt, t.UpdatedBy, t.Versions))));

        texts.MapGet("/{key}", async (string key, PanelTools tools, CancellationToken ct) =>
        {
            if (!TextKey().IsMatch(key))
            {
                return Results.NotFound(new { error = "not_found" });
            }

            var history = await tools.HistoryAsync(key, ct);
            return history.Count == 0
                ? Results.NotFound(new { error = "not_found" })
                : Results.Ok(history.Select(v => new TextVersionDto(v.Version, v.Text, v.Active, v.Source, v.UpdatedAt, v.UpdatedBy)));
        });

        texts.MapPost("/{key}", async (string key, TextSaveRequest? body, HttpContext ctx, PanelTools tools, CancellationToken ct) =>
        {
            if (!TextKey().IsMatch(key))
            {
                return Results.NotFound(new { error = "not_found" });
            }

            var me = ctx.CurrentUser();
            var (error, version) = await tools.SaveTextAsync(me.Id, me.Username, key, body?.Text, ct);
            return error switch
            {
                null => Results.Ok(new { version }),
                PanelTools.ErrUnknownText => Results.NotFound(new { error = "not_found" }),
                _ => Results.BadRequest(new { error }),
            };
        });

        texts.MapPost("/{key}/versions/{version:int}/activate", async (string key, int version, HttpContext ctx, PanelTools tools, CancellationToken ct) =>
        {
            if (!TextKey().IsMatch(key))
            {
                return Results.NotFound(new { error = "not_found" });
            }

            var me = ctx.CurrentUser();
            return await tools.ActivateTextAsync(me.Id, me.Username, key, version, ct) is null
                ? Results.NoContent()
                : Results.NotFound(new { error = "not_found" });
        });

        // ---------- reports ----------
        panel.MapGet("/reports", async (string? month, string? from, string? to, PanelTools tools, TimeProvider clock, CancellationToken ct) =>
        {
            var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Office.Domain.Tasks.OfficeHours.Cairo);
            // the owner (2026-11-07): a day, a range or a year too: «from» and «to» (YYYY-MM-DD, both included, up to 366 days)
            if (from is { Length: > 0 } || to is { Length: > 0 })
            {
                var fmt = System.Globalization.CultureInfo.InvariantCulture;
                if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", fmt, System.Globalization.DateTimeStyles.None, out var f)
                    || !DateOnly.TryParseExact(to ?? from, "yyyy-MM-dd", fmt, System.Globalization.DateTimeStyles.None, out var t)
                    || f.Year < 2024 || t < f || t.DayNumber - f.DayNumber > 365)
                {
                    return Results.BadRequest(new { error = "invalid_range" });
                }

                return Results.Ok(await tools.ReportAsync(f, t, f == t ? f.ToString("yyyy-MM-dd", fmt) : $"{f:yyyy-MM-dd}..{t:yyyy-MM-dd}", ct));
            }

            int year = now.Year, m = now.Month;
            if (month is { Length: > 0 })
            {
                var parts = month.Split('-');
                if (parts.Length != 2 || !int.TryParse(parts[0], out year) || !int.TryParse(parts[1], out m)
                    || year is < 2024 or > 2100 || m is < 1 or > 12)
                {
                    return Results.BadRequest(new { error = "invalid_month" });
                }
            }

            return Results.Ok(await tools.ReportAsync(year, m, ct));
        }).RequireAuthorization(PanelAuth.Policy(Permissions.ReportsView));

        // «الشهور»: what came each month, the last 12 months up to this one
        panel.MapGet("/reports/trend", async (PanelTools tools, TimeProvider clock, CancellationToken ct) =>
        {
            var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Office.Domain.Tasks.OfficeHours.Cairo);
            return Results.Ok(new { months = await tools.TrendAsync(now.Year, now.Month, 12, ct) });
        }).RequireAuthorization(PanelAuth.Policy(Permissions.ReportsView));

        // v1.58 (the owner: «المحادثة صرفت قد إيه من الكريدت؟»): the AI cost by conversation, and one conversation's messages
        panel.MapGet("/reports/ai-cost", async (string? month, PanelTools tools, TimeProvider clock, CancellationToken ct) =>
            MonthOf(month, clock) is { } m ? Results.Ok(await tools.AiCostAsync(m.Year, m.Month, ct)) : Results.BadRequest(new { error = "invalid_month" }))
            .RequireAuthorization(PanelAuth.Policy(Permissions.ReportsView));
        panel.MapGet("/reports/ai-cost/{personId:guid}", async (Guid personId, string? month, PanelTools tools, TimeProvider clock, CancellationToken ct) =>
            MonthOf(month, clock) is { } m ? Results.Ok(new { turns = await tools.AiCostTurnsAsync(personId, m.Year, m.Month, ct) })
                : Results.BadRequest(new { error = "invalid_month" }))
            .RequireAuthorization(PanelAuth.Policy(Permissions.ReportsView));
    }

    /// <summary>The month asked («2026-11»), this month when none; null when it is not a month from 2024 on.</summary>
    private static (int Year, int Month)? MonthOf(string? month, TimeProvider clock)
    {
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Office.Domain.Tasks.OfficeHours.Cairo);
        if (month is not { Length: > 0 })
        {
            return (now.Year, now.Month);
        }

        var parts = month.Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m) && y is >= 2024 and <= 2100 && m is >= 1 and <= 12
            ? (y, m)
            : null;
    }

    public sealed record UnmuteRequest(string? WaId);

    public sealed record ListEntryRequest(string? Kind, string? Phone, string? Name, string? StaffName, string? Note);

    public sealed record ListImportRequest(string? Kind, string? Csv, bool Replace);

    public sealed record TextSaveRequest(string? Text);

    public sealed record ListEntryDto(Guid Id, string Kind, string WaId, string? Name, string? StaffName, string? Note, DateTimeOffset ImportedAt);

    public sealed record MutedDto(string WaId, string? Name, DateTimeOffset Since);

    public sealed record TextDto(string Key, int Version, string Text, int MaxChars, string Source, DateTimeOffset UpdatedAt, string? UpdatedBy, int Versions);

    public sealed record TextVersionDto(int Version, string Text, bool Active, string Source, DateTimeOffset UpdatedAt, string? UpdatedBy);
}
