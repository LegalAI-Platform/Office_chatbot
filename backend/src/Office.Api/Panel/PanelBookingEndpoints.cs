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

        // TASK 09 (B-05, B-22, S-04): the payments. Everyone sees them, reviews a transfer (P1) and records a refund (P2).
        var payments = panel.MapGroup("/payments").RequireAuthorization(PanelAuth.Policy(Permissions.ScheduleManage));

        payments.MapGet("", async (string? show, PanelPayments p, CancellationToken ct) =>
            await p.ListAsync(show, ct) is { } list ? Results.Ok(list) : Results.BadRequest(new { error = PanelPayments.ErrInvalid }));

        // A receipt image (0 = the first), like a client's file in 07c: never cached, shown only as a picture, recorded.
        payments.MapGet("/{id:guid}/receipts/{index:int}", async (Guid id, int index, HttpContext ctx, PanelPayments p,
            Office.Application.Panel.IClientFiles files, CancellationToken ct) =>
        {
            if (!files.Configured)
            {
                return Results.Json(new { error = "not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (await p.ReceiptAsync(id, index, ct) is not { } receipt || await files.OpenAsync(receipt, ct) is not { } file)
            {
                return Results.NotFound(new { error = PanelPayments.ErrNotFound });
            }

            await p.RecordOpenedAsync(ctx.CurrentUser().Id, id, index, ct);
            var type = ClientFileTypes.Safe(file.ContentType);
            ctx.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'";
            return type.StartsWith("image/", StringComparison.Ordinal)
                ? Results.Stream(file.Content, type)
                : Results.Stream(file.Content, "application/octet-stream", $"receipt-{index + 1}");
        });

        payments.MapPost("/{id:guid}/review", async (Guid id, ReviewRequest? body, HttpContext ctx, PanelPayments p, CancellationToken ct) =>
            PaymentAnswer(await p.ReviewAsync(ctx.CurrentUser().Id, id, body?.State, body?.Received, ct)))
            .RequireAuthorization(PanelAuth.Policy(Permissions.PaymentsVerify));

        payments.MapPost("/{id:guid}/refunded", async (Guid id, RefundedRequest? body, HttpContext ctx, PanelPayments p, CancellationToken ct) =>
            PaymentAnswer(await p.RefundedAsync(ctx.CurrentUser().Id, id, body?.Method, body?.Note, ct)))
            .RequireAuthorization(PanelAuth.Policy(Permissions.PaymentsRefund));
    }

    /// <summary>
    /// TASK 10 (B-16, B-17, S-07): the service orders. Everyone sees them, opens the client's documents, uploads and
    /// delivers the file and closes an order; the lawyer and the manager price one and ask for a written opinion.
    /// </summary>
    public static void MapPanelServices(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/panel")
            .AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<AntiforgeryFilter>()
            .AddEndpointFilter<MustChangePasswordFilter>()
            .AddEndpointFilter<TwoFactorRequiredFilter>();
        var services = panel.MapGroup("/services").RequireAuthorization(PanelAuth.Policy(Permissions.ServicesManage));

        services.MapGet("", async (string? show, Office.Infrastructure.Services.PanelServices s, CancellationToken ct) =>
            await s.ListAsync(show, ct) is { } list ? Results.Ok(list) : Results.BadRequest(new { error = PanelPayments.ErrInvalid }));

        // A document the client sent for the order, like a client's file in 07c: never cached, recorded once read.
        services.MapGet("/{id:guid}/files/{index:int}", async (Guid id, int index, HttpContext ctx, Office.Infrastructure.Services.PanelServices s,
            Office.Application.Panel.IClientFiles files, CancellationToken ct) =>
            await ClientDocumentAsync(ctx, files, index, () => s.FileAsync(id, index, ct), () => s.RecordFileOpenedAsync(ctx.CurrentUser().Id, id, index, ct), ct));

        services.MapPost("/{id:guid}/price", async (Guid id, PriceRequest? body, HttpContext ctx, Office.Infrastructure.Services.PanelServices s,
            CancellationToken ct) =>
            ServiceAnswer(await s.PriceAsync(ctx.CurrentUser().Id, id, body?.Price, body?.Exception == true, Day(body?.DueOn), body?.Delivery, ct)))
            .RequireAuthorization(PanelAuth.Policy(Permissions.ServicesPrice));

        services.MapPost("/{id:guid}/upload", async (Guid id, UploadRequest? body, Office.Infrastructure.Services.PanelServices s, CancellationToken ct) =>
            await s.UploadAsync(id, body?.Filename, ct) is var r && r.Error is null
                ? Results.Ok(new { url = r.Url, key = r.Key, content_type = r.ContentType })
                : ServiceAnswer(r.Error));

        services.MapPost("/{id:guid}/deliver", async (Guid id, DeliverRequest? body, HttpContext ctx, Office.Infrastructure.Services.PanelServices s,
            CancellationToken ct) =>
            ServiceAnswer(await s.DeliverAsync(ctx.CurrentUser().Id, id, body?.Key, body?.Filename, ct)));

        services.MapPost("/{id:guid}/resend", async (Guid id, HttpContext ctx, Office.Infrastructure.Services.PanelServices s, CancellationToken ct) =>
            ServiceAnswer(await s.ResendAsync(ctx.CurrentUser().Id, id, ct)));

        services.MapPost("/{id:guid}/close", async (Guid id, HttpContext ctx, Office.Infrastructure.Services.PanelServices s, CancellationToken ct) =>
            ServiceAnswer(await s.CloseAsync(ctx.CurrentUser().Id, id, ct)));

        // B-17: on a consultation that took place.
        panel.MapPost("/booking/appointments/{id:guid}/opinion", async (Guid id, OpinionRequest? body, HttpContext ctx,
            Office.Infrastructure.Services.PanelServices s, CancellationToken ct) =>
            ServiceAnswer(await s.OpinionAsync(ctx.CurrentUser().Id, id, Day(body?.DueOn), body?.Delivery, ct)))
            .RequireAuthorization(PanelAuth.Policy(Permissions.ServicesPrice));
    }

    /// <summary>TASK 11 (DECISIONS v1.15, B-11 / S-06): «حالات العرفي». docs/panel-api.md.</summary>
    public static void MapPanelUrf(this IEndpointRouteBuilder app)
    {
        var urf = app.MapGroup("/api/panel/urf")
            .AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<AntiforgeryFilter>()
            .AddEndpointFilter<MustChangePasswordFilter>()
            .AddEndpointFilter<TwoFactorRequiredFilter>()
            .RequireAuthorization(PanelAuth.Policy(Permissions.UrfManage));

        urf.MapGet("", async (string? show, Office.Infrastructure.Urf.PanelUrf u, CancellationToken ct) =>
            await u.ListAsync(show, ct) is { } list ? Results.Ok(list) : Results.BadRequest(new { error = PanelPayments.ErrInvalid }));

        // A document the client sent for the case (ID cards): never cached, recorded once read.
        urf.MapGet("/{id:guid}/files/{index:int}", async (Guid id, int index, HttpContext ctx, Office.Infrastructure.Urf.PanelUrf u,
            Office.Application.Panel.IClientFiles files, CancellationToken ct) =>
            await ClientDocumentAsync(ctx, files, index, () => u.FileAsync(id, index, ct), () => u.RecordFileOpenedAsync(ctx.CurrentUser().Id, id, index, ct), ct));

        urf.MapPost("/{id:guid}/review", async (Guid id, UrfReviewRequest? body, HttpContext ctx, Office.Infrastructure.Urf.PanelUrf u,
            CancellationToken ct) => ServiceAnswer(await u.ReviewAsync(ctx.CurrentUser().Id, id, body?.Decision, ct)))
            .RequireAuthorization(PanelAuth.Policy(Permissions.UrfReview));
    }

    public sealed record UrfReviewRequest(string? Decision);

    /// <summary>
    /// A document a client sent (a service order's, an Urfi case's), like a client's file in 07c: never cached, sandboxed,
    /// a picture or a PDF inline and anything else as a download, and every opening recorded after it was read.
    /// </summary>
    private static async Task<IResult> ClientDocumentAsync(HttpContext ctx, Office.Application.Panel.IClientFiles files, int index,
        Func<Task<Office.Application.Panel.ClientFileRef?>> find, Func<Task> opened, CancellationToken ct)
    {
        if (!files.Configured)
        {
            return Results.Json(new { error = "not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (await find() is not { } f || await files.OpenAsync(f, ct) is not { } file)
        {
            return Results.NotFound(new { error = PanelPayments.ErrNotFound });
        }

        await opened();
        var type = ClientFileTypes.Safe(file.ContentType);
        ctx.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'";
        return Results.Stream(file.Content, type, ClientFileTypes.Inline(type) ? null : ClientFileTypes.Name(index, file.Filename, type));
    }

    private static IResult ServiceAnswer(string? error) => error switch
    {
        null => Results.NoContent(),
        Office.Infrastructure.Services.PanelServices.ErrNotFound => Results.NotFound(new { error }),
        Office.Infrastructure.Services.PanelServices.ErrNotAllowed => Results.Conflict(new { error }),
        Office.Infrastructure.Services.PanelServices.ErrNotConfigured => Results.Json(new { error }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.BadRequest(new { error }),
    };

    public sealed record PriceRequest(int? Price, bool Exception, string? DueOn, string? Delivery);

    public sealed record UploadRequest(string? Filename);

    public sealed record DeliverRequest(string? Key, string? Filename);

    public sealed record OpinionRequest(string? DueOn, string? Delivery);

    private static IResult PaymentAnswer(string? error) => error switch
    {
        null => Results.NoContent(),
        PanelPayments.ErrNotFound => Results.NotFound(new { error }),
        PanelPayments.ErrNotAllowed => Results.Conflict(new { error }),
        _ => Results.BadRequest(new { error }),
    };

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

    public sealed record ReviewRequest(string? State, int? Received);

    public sealed record RefundedRequest(string? Method, string? Note);
}
