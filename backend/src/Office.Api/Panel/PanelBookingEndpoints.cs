using System.Globalization;
using Office.Application.Panel;
using Office.Infrastructure.Booking;

namespace Office.Api.Panel;

/// <summary>
/// TASK 08 (DECISIONS v1.12, B-24): the booking hours and the appointments from the panel. Everyone
/// with schedule.manage (the manager, the lawyer and the staff) changes everything; the office's
/// payment details need office.manage (P1). The same filters as the rest of /api/panel.
/// </summary>
public static class PanelBookingEndpoints
{
    public static void MapPanelBooking(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/panel")
            .AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<AntiforgeryFilter>()
            .AddEndpointFilter<MustChangePasswordFilter>()
            .AddEndpointFilter<TwoFactorRequiredFilter>();

        var booking = panel.MapGroup("/booking").RequireAuthorization(PanelAuth.Policy(Permissions.ScheduleManage));

        booking.MapGet("/week", async (PanelBooking b, CancellationToken ct) => Results.Ok(await b.WeekAsync(ct)));

        booking.MapPut("/week", async (WeekRequest? body, HttpContext ctx, PanelBooking b, CancellationToken ct) =>
            Answer(await b.SaveWeekAsync(ctx.CurrentUser().Id, body?.Days, body?.Confirm == true, ct)));

        booking.MapGet("/month", async (string? month, PanelBooking b, TimeProvider clock, CancellationToken ct) =>
            ParseMonth(month, clock) is { } m ? Results.Ok(await b.MonthAsync(m.Year, m.Month, ct)) : Results.BadRequest(new { error = "invalid_month" }));

        booking.MapPost("/exceptions", async (ExceptionRequest? body, HttpContext ctx, PanelBooking b, CancellationToken ct) =>
            body is null || !DateOnly.TryParseExact(body.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? Results.BadRequest(new { error = PanelBooking.ErrInvalid })
                : Answer(await b.AddExceptionAsync(ctx.CurrentUser().Id, day, body.Kind, body.Start, body.End, body.Reason, body.Confirm, ct)));

        booking.MapDelete("/exceptions/{id:guid}", async (Guid id, bool? confirm, HttpContext ctx, PanelBooking b, CancellationToken ct) =>
            Answer(await b.RemoveExceptionAsync(ctx.CurrentUser().Id, id, confirm == true, ct)));

        booking.MapGet("/settings", async (PanelBooking b, CancellationToken ct) => Results.Ok(await b.SettingsAsync(ct)));

        booking.MapPut("/settings", async (BookingSettingsView? body, HttpContext ctx, PanelBooking b, CancellationToken ct) =>
        {
            if (body is null)
            {
                return Results.BadRequest(new { error = PanelBooking.ErrInvalid });
            }

            var me = ctx.CurrentUser();
            return await b.SaveSettingsAsync(me.Id, me.Username, body, ct) is { } error ? Results.BadRequest(new { error }) : Results.NoContent();
        });

        booking.MapGet("/changes", async (PanelBooking b, CancellationToken ct) => Results.Ok(await b.ChangesAsync(ct)));

        booking.MapPost("/undo", async (UndoRequest? body, HttpContext ctx, PanelBooking b, CancellationToken ct) =>
        {
            var me = ctx.CurrentUser();
            return Answer(await b.UndoAsync(me.Id, me.Username, body?.Confirm == true, ct));
        });

        booking.MapGet("/appointments", async (string? from, string? to, PanelBooking b, TimeProvider clock, CancellationToken ct) =>
        {
            var today = Office.Application.Booking.BookingDesk.Today(clock.GetUtcNow());
            var f = Day(from) ?? today;
            var t = Day(to) ?? f.AddDays(13);
            return t < f || t.DayNumber - f.DayNumber > 92
                ? Results.BadRequest(new { error = PanelBooking.ErrInvalid })
                : Results.Ok(await b.AppointmentsAsync(f, t, ct));
        });

        booking.MapPost("/appointments/{id:guid}/state", async (Guid id, StateRequest? body, HttpContext ctx, PanelBooking b, CancellationToken ct) =>
            await b.MarkAsync(ctx.CurrentUser().Id, id, body?.State, ct) switch
            {
                null => Results.NoContent(),
                PanelBooking.ErrNotFound => Results.NotFound(new { error = PanelBooking.ErrNotFound }),
                PanelBooking.ErrInvalid => Results.BadRequest(new { error = PanelBooking.ErrInvalid }),
                var e => Results.Conflict(new { error = e }),
            });

        booking.MapPost("/appointments/{id:guid}/cancel", async (Guid id, HttpContext ctx, PanelBooking b, CancellationToken ct) =>
            await b.CancelByOfficeAsync(ctx.CurrentUser().Id, id, ct) switch
            {
                { Error: null } r => Results.Ok(new { cancelled = r.Cancelled }),
                { Error: PanelBooking.ErrNotFound } => Results.NotFound(new { error = PanelBooking.ErrNotFound }),
                var r => Results.Conflict(new { error = r.Error }),
            });

        booking.MapGet("/preview", async (string? day, string? service, PanelBooking b, CancellationToken ct) =>
            Day(day) is { } d ? Results.Ok(await b.PreviewAsync(d, service, ct)) : Results.BadRequest(new { error = PanelBooking.ErrInvalid }));

        // P1: the office's payment details and address (they go into Hazem's messages).
        var office = panel.MapGroup("/office").RequireAuthorization(PanelAuth.Policy(Permissions.OfficeManage));

        office.MapGet("", async (PanelBooking b, CancellationToken ct) => Results.Ok(await b.OfficeAsync(ct)));

        office.MapPut("", async (OfficeView? body, HttpContext ctx, PanelBooking b, CancellationToken ct) =>
        {
            if (body is null)
            {
                return Results.BadRequest(new { error = PanelBooking.ErrInvalid });
            }

            var me = ctx.CurrentUser();
            return await b.SaveOfficeAsync(me.Id, me.Username, body, ct) is { } error ? Results.BadRequest(new { error }) : Results.NoContent();
        });
    }

    /// <summary>
    /// Done (the clients of cancelled appointments are told by the worker within seconds), or 409 with the
    /// appointments the change would cancel (the panel asks, then sends confirm).
    /// </summary>
    private static IResult Answer(ChangeResult r) => r switch
    {
        { Error: null } => Results.Ok(new { cancelled = r.Cancelled }),
        { Error: PanelBooking.ErrHasBookings } => Results.Conflict(new { error = r.Error, appointments = r.Affected }),
        { Error: PanelBooking.ErrNotFound } => Results.NotFound(new { error = r.Error }),
        { Error: PanelBooking.ErrNothingToUndo } => Results.Conflict(new { error = r.Error }),
        _ => Results.BadRequest(new { error = r.Error }),
    };

    private static DateOnly? Day(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static (int Year, int Month)? ParseMonth(string? month, TimeProvider clock)
    {
        if (string.IsNullOrEmpty(month))
        {
            var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Office.Domain.Tasks.OfficeHours.Cairo);
            return (now.Year, now.Month);
        }

        var parts = month.Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m) && y is >= 2024 and <= 2100 && m is >= 1 and <= 12
            ? (y, m)
            : null;
    }

    public sealed record WeekRequest(IReadOnlyList<WeekDayView>? Days, bool Confirm);

    public sealed record ExceptionRequest(string? Day, string? Kind, int? Start, int? End, string? Reason, bool Confirm);

    public sealed record StateRequest(string? State);

    public sealed record UndoRequest(bool Confirm);
}
