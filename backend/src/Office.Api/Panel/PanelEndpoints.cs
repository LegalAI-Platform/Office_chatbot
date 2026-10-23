using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Office.Application.Chatwoot;
using Office.Application.Panel;

namespace Office.Api.Panel;

/// <summary>
/// /api/panel (docs/panel-api.md): what the React panel calls through nginx. Every endpoint names
/// its permission (RBAC, T-23); a test walks all of them. JSON is snake_case like the rest of the API.
/// </summary>
public static class PanelEndpoints
{
    public static void MapPanelApi(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/panel")
            .AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<AntiforgeryFilter>();

        // ---------- anonymous: the antiforgery token and sign-in ----------
        panel.MapGet("/auth/csrf", (HttpContext ctx, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(ctx).RequestToken }))
            .AllowAnonymous();

        panel.MapPost("/auth/login", async (LoginRequest? body, HttpContext ctx, PanelAccounts accounts, IDataProtectionProvider dp, TimeProvider clock, CancellationToken ct) =>
        {
            var r = await accounts.SignInAsync(body?.Username, body?.Password, ct);
            if (r.Outcome != PanelOutcome.Ok)
            {
                // Only the right password learns that a registered account still waits (v1.9).
                return r.Error is PanelAccounts.EmailNotVerified or PanelAccounts.PendingApproval
                    ? Results.Json(new { error = r.Error, email = r.Error == PanelAccounts.EmailNotVerified ? r.Value?.Email : null },
                        statusCode: StatusCodes.Status403Forbidden)
                    : Results.Json(new { error = PanelAccounts.InvalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = r.Value!;
            if (user.TotpEnabled)
            {
                // No session yet: a five-minute ticket for the second step, tied to this password (the stamp).
                var until = (clock.GetUtcNow() + PanelAuth.SecondStepFor).ToUnixTimeSeconds();
                var ticket = SecondStepTicket(dp).Protect($"{user.Id:N}|{user.SecurityStamp}|{until}");
                return Results.Ok(new { two_factor = true, ticket });
            }

            await PanelAuth.SignInAsync(ctx, user);
            return Results.Ok(Me(user, ctx));
        })
        .AllowAnonymous()
        .RequireRateLimiting(PanelAuth.LoginLimiter);

        panel.MapPost("/auth/login/2fa", async (SecondStepRequest? body, HttpContext ctx, PanelTwoFactor twoFactor, IDataProtectionProvider dp, TimeProvider clock, CancellationToken ct) =>
        {
            var parts = Unticket(dp, body?.Ticket)?.Split('|');
            if (parts is not [var id, var stamp, var until] || !Guid.TryParseExact(id, "N", out var userId)
                || !long.TryParse(until, out var untilSeconds) || clock.GetUtcNow().ToUnixTimeSeconds() > untilSeconds)
            {
                return Results.Json(new { error = "expired_ticket" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var r = await twoFactor.SecondStepAsync(userId, stamp, body?.Code, ct);
            if (r.Outcome != PanelOutcome.Ok)
            {
                return Results.Json(new { error = PanelSignup.InvalidCode }, statusCode: StatusCodes.Status401Unauthorized);
            }

            await PanelAuth.SignInAsync(ctx, r.Value!);
            return Results.Ok(Me(r.Value!, ctx));
        })
        .AllowAnonymous()
        .RequireRateLimiting(PanelAuth.LoginLimiter);

        // ---------- anonymous: sign-up and a forgotten password (v1.9); codes by e-mail ----------
        var open = panel.MapGroup("/auth").AllowAnonymous().RequireRateLimiting(PanelAuth.LoginLimiter);

        open.MapGet("/signup", (PanelSignup signup) => Results.Ok(new { available = signup.Ready }));

        open.MapPost("/register", async (RegisterRequest? body, PanelSignup signup, CancellationToken ct) =>
        {
            var r = await signup.RegisterAsync(new PanelSignup.Registration(body?.Username, body?.DisplayName, body?.Email, body?.Password), ct);
            return r.Outcome == PanelOutcome.Ok ? Results.Accepted(value: new { mail_sent = r.Value }) : Refused(r);
        });

        open.MapPost("/register/resend", async (EmailRequest? body, PanelSignup signup, CancellationToken ct) =>
        {
            var r = await signup.ResendAsync(body?.Email, ct);
            return r.Outcome == PanelOutcome.Ok ? Results.Accepted() : Refused(r);
        });

        open.MapPost("/register/verify", async (CodeRequest? body, PanelSignup signup, CancellationToken ct) =>
        {
            var r = await signup.VerifyEmailAsync(body?.Email, body?.Code, ct);
            return r.Outcome == PanelOutcome.Ok ? Results.NoContent() : Refused(r);
        });

        open.MapPost("/forgot", async (ForgotRequest? body, PanelSignup signup, CancellationToken ct) =>
        {
            var r = await signup.ForgotAsync(body?.Login, ct);
            return r.Outcome == PanelOutcome.Ok ? Results.Accepted() : Refused(r);
        });

        open.MapPost("/reset", async (ResetRequest? body, PanelSignup signup, CancellationToken ct) =>
        {
            var r = await signup.ResetAsync(body?.Login, body?.Code, body?.NewPassword, ct);
            return r.Outcome == PanelOutcome.Ok ? Results.NoContent() : Refused(r);
        });

        // ---------- the signed-in account ----------
        var self = panel.MapGroup("/auth").RequireAuthorization(PanelAuth.Policy(Permissions.AccountSelf));

        self.MapGet("/me", (HttpContext ctx) => Results.Ok(Me(ctx.CurrentUser(), ctx)));

        self.MapPost("/logout", async (HttpContext ctx, PanelAccounts accounts, CancellationToken ct) =>
        {
            await accounts.AuditAsync(ctx.CurrentUser().Id, "logout", ct);
            await ctx.SignOutAsync(PanelAuth.Scheme);
            return Results.NoContent();
        });

        self.MapPost("/change-password", async (ChangePasswordRequest? body, HttpContext ctx, PanelAccounts accounts, CancellationToken ct) =>
        {
            var r = await accounts.ChangePasswordAsync(ctx.CurrentUser().Id, body?.CurrentPassword, body?.NewPassword, ct);
            if (r.Outcome != PanelOutcome.Ok)
            {
                return Refused(r);
            }

            // Every other session ends (new stamp); this one continues with a fresh cookie.
            await PanelAuth.SignInAsync(ctx, r.Value!);
            return Results.NoContent();
        });

        // ---------- everything else: not until a temporary password is changed ----------
        var work = panel.MapGroup("").AddEndpointFilter<MustChangePasswordFilter>().AddEndpointFilter<TwoFactorRequiredFilter>();

        // ---------- the authenticator app (v1.9), from «حسابي» ----------
        // Outside «work»: a manager who must turn it on can still reach it (after a temporary password).
        var twoFa = panel.MapGroup("/auth/2fa").AddEndpointFilter<MustChangePasswordFilter>()
            .RequireAuthorization(PanelAuth.Policy(Permissions.AccountSelf));

        twoFa.MapGet("", async (HttpContext ctx, PanelTwoFactor tf, IPanelCrypto crypto, CancellationToken ct) =>
        {
            var s = await tf.StatusAsync(ctx.CurrentUser(), ct);
            return Results.Ok(new { enabled = s.Enabled, recovery_codes_left = s.RecoveryCodesLeft, available = crypto.Configured });
        });

        twoFa.MapPost("/setup", async (HttpContext ctx, PanelTwoFactor tf, CancellationToken ct) =>
        {
            var r = await tf.SetupAsync(ctx.CurrentUser().Id, ct);
            return r.Outcome == PanelOutcome.Ok ? Results.Ok(new { secret = r.Value!.Secret, uri = r.Value.Uri }) : Refused(r);
        });

        twoFa.MapPost("/enable", async (OneCodeRequest? body, HttpContext ctx, PanelTwoFactor tf, PanelAccounts accounts, CancellationToken ct) =>
        {
            var r = await tf.EnableAsync(ctx.CurrentUser().Id, body?.Code, ct);
            if (r.Outcome != PanelOutcome.Ok)
            {
                return Refused(r);
            }

            // Every other session ended (new stamp); this one continues with a fresh cookie.
            await PanelAuth.SignInAsync(ctx, (await accounts.FindAsync(ctx.CurrentUser().Id, ct))!);
            return Results.Ok(new { recovery_codes = r.Value });
        });

        twoFa.MapPost("/disable", async (DisableTwoFactorRequest? body, HttpContext ctx, PanelTwoFactor tf, PanelAccounts accounts, CancellationToken ct) =>
        {
            var r = await tf.DisableAsync(ctx.CurrentUser().Id, body?.Password, body?.Code, ct);
            if (r.Outcome != PanelOutcome.Ok)
            {
                return Refused(r);
            }

            await PanelAuth.SignInAsync(ctx, (await accounts.FindAsync(ctx.CurrentUser().Id, ct))!);
            return Results.NoContent();
        });

        twoFa.MapPost("/recovery-codes", async (OneCodeRequest? body, HttpContext ctx, PanelTwoFactor tf, CancellationToken ct) =>
        {
            var r = await tf.RenewRecoveryCodesAsync(ctx.CurrentUser().Id, body?.Code, ct);
            return r.Outcome == PanelOutcome.Ok ? Results.Ok(new { recovery_codes = r.Value }) : Refused(r);
        });

        var tasks = work.MapGroup("/tasks");
        tasks.MapGet("", async (string? scope, string? state, int? page, string? q, HttpContext ctx, PanelTasks use, CancellationToken ct) =>
        {
            if (!Enum.TryParse<TaskScope>(scope ?? "mine", true, out var sc) || !Enum.TryParse<TaskStateFilter>(state ?? "active", true, out var st))
            {
                return Results.BadRequest(new { error = "invalid_filter" });
            }

            var r = await use.ListAsync(ctx.CurrentUser().AsViewer(), new TaskQuery(sc, st, Math.Clamp(page ?? 1, 1, 10_000), Search: q is { Length: > 0 and <= 60 } ? q : null), ct);
            return r.Outcome == PanelOutcome.Ok ? Results.Ok(TaskPageDto.From(r.Value!)) : Refused(r);
        }).RequireAuthorization(PanelAuth.Policy(Permissions.TasksViewOwn));

        tasks.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, PanelTasks use, CancellationToken ct) =>
            Answer(await use.GetAsync(ctx.CurrentUser().AsViewer(), id, ct)))
            .RequireAuthorization(PanelAuth.Policy(Permissions.TasksViewOwn));

        foreach (var action in new[] { PanelTasks.ActionStart, PanelTasks.ActionDone, PanelTasks.ActionCancel })
        {
            tasks.MapPost($"/{{id:guid}}/{action}", async (Guid id, HttpContext ctx, PanelTasks use, CancellationToken ct) =>
                Answer(await use.ActAsync(ctx.CurrentUser().AsViewer(), id, action, ct)))
                .RequireAuthorization(PanelAuth.Policy(Permissions.TasksUpdate));
        }

        tasks.MapPost("/{id:guid}/assign", async (Guid id, AssignRequest? body, HttpContext ctx, PanelTasks use, CancellationToken ct) =>
            Answer(await use.AssignAsync(ctx.CurrentUser().AsViewer(), id, body?.StaffId, ct)))
            .RequireAuthorization(PanelAuth.Policy(Permissions.TasksAssign));

        // The home screen: counts, the last 7 days, the latest tasks and the ones that need doing first.
        work.MapGet("/dashboard", async (HttpContext ctx, PanelTasks use, CancellationToken ct) =>
            Results.Ok(DashboardDto.From(await use.DashboardAsync(ctx.CurrentUser().AsViewer(), ct))))
            .RequireAuthorization(PanelAuth.Policy(Permissions.TasksViewOwn));

        work.MapGet("/staff", async (PanelTasks use, CancellationToken ct) =>
            Results.Ok((await use.StaffAsync(ct)).Select(s => new StaffDto(s.Id, s.Name))))
            .RequireAuthorization(PanelAuth.Policy(Permissions.TasksAssign));

        // ---------- accounts (the manager) ----------
        var users = work.MapGroup("/users").RequireAuthorization(PanelAuth.Policy(Permissions.UsersManage));

        users.MapGet("", async (PanelAccounts accounts, TimeProvider clock, CancellationToken ct) =>
            Results.Ok((await accounts.ListAsync(ct)).Select(u => UserDto.From(u, clock.GetUtcNow()))));

        users.MapPost("", async (NewUserRequest? body, HttpContext ctx, PanelAccounts accounts, TimeProvider clock, CancellationToken ct) =>
        {
            var r = await accounts.CreateAsync(ctx.CurrentUser().Id,
                new PanelAccounts.NewAccount(body?.Username, body?.DisplayName, body?.Email, body?.Role, body?.StaffName), null, ct);
            if (r.Outcome == PanelOutcome.Ok)
            {
                await SyncChatwoot(ctx, r.Value.User);
            }

            return r.Outcome == PanelOutcome.Ok
                ? Results.Ok(new { user = UserDto.From(r.Value.User, clock.GetUtcNow()), temporary_password = r.Value.TemporaryPassword })
                : Refused(r);
        });

        users.MapPost("/{id:guid}/disable", async (Guid id, HttpContext ctx, PanelAccounts a, TimeProvider clock, CancellationToken ct) =>
            await Synced(ctx, await a.DisableAsync(ctx.CurrentUser().Id, id, ct), clock));
        users.MapPost("/{id:guid}/enable", async (Guid id, HttpContext ctx, PanelAccounts a, TimeProvider clock, CancellationToken ct) =>
            await Synced(ctx, await a.EnableAsync(ctx.CurrentUser().Id, id, ct), clock));
        users.MapPost("/{id:guid}/role", async (Guid id, RoleRequest? body, HttpContext ctx, PanelAccounts a, TimeProvider clock, CancellationToken ct) =>
            await Synced(ctx, await a.ChangeRoleAsync(ctx.CurrentUser().Id, id, body?.Role, ct), clock));
        users.MapPost("/{id:guid}/reset-password", async (Guid id, HttpContext ctx, PanelAccounts a, CancellationToken ct) =>
        {
            var r = await a.ResetPasswordAsync(ctx.CurrentUser().Id, id, null, ct);
            return r.Outcome == PanelOutcome.Ok ? Results.Ok(new { temporary_password = r.Value }) : Refused(r);
        });
        // v1.9: a registered account waits for the manager, who picks its role (or refuses it).
        users.MapPost("/{id:guid}/approve", async (Guid id, ApproveRequest? body, HttpContext ctx, PanelSignup s, TimeProvider clock, CancellationToken ct) =>
            await Synced(ctx, await s.ApproveAsync(ctx.CurrentUser().Id, id, body?.Role, body?.StaffName, ct), clock));
        users.MapPost("/{id:guid}/reject", async (Guid id, HttpContext ctx, PanelSignup s, CancellationToken ct) =>
        {
            var r = await s.RejectAsync(ctx.CurrentUser().Id, id, ct);
            return r.Outcome == PanelOutcome.Ok ? Results.NoContent() : Refused(r);
        });
        users.MapPost("/{id:guid}/reset-2fa", async (Guid id, HttpContext ctx, PanelTwoFactor tf, TimeProvider clock, CancellationToken ct) =>
            UserAnswer(await tf.ResetAsync(ctx.CurrentUser().Id, id, ct), clock));
    }

    private static MeDto Me(PanelUser u, HttpContext ctx) =>
        new(u.Id, u.Username, u.DisplayName, u.Email, u.Role, Permissions.Of(u.Role), u.MustChangePassword, u.TotpEnabled,
            PanelAuth.NeedsTwoFactor(ctx, u));

    /// <summary>Encrypted and signed; the expiry inside is checked against the API's clock.</summary>
    private static IDataProtector SecondStepTicket(IDataProtectionProvider dp) => dp.CreateProtector("panel.login.second-step");

    private static string? Unticket(IDataProtectionProvider dp, string? ticket)
    {
        if (string.IsNullOrEmpty(ticket) || ticket.Length > 1000)
        {
            return null;
        }

        try
        {
            return SecondStepTicket(dp).Unprotect(ticket);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null; // expired, forged, or made before the API restarted
        }
    }

    private static IResult Answer(PanelResult<TaskView> r) =>
        r.Outcome == PanelOutcome.Ok ? Results.Ok(TaskDetailDto.From(r.Value!)) : Refused(r);

    /// <summary>
    /// TASK 07b: the account's Chatwoot user follows the change (made, re-roled, taken out). The
    /// panel never waits on Chatwoot: a failure is logged and the next sync fixes it.
    /// </summary>
    private static async Task SyncChatwoot(HttpContext ctx, PanelUser user)
    {
        try
        {
            await ctx.RequestServices.GetRequiredService<ChatwootAccounts>().SyncAsync(user, ctx.RequestAborted);
        }
        catch (Exception e) when (!ctx.RequestAborted.IsCancellationRequested)
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Office.Api.Chatwoot")
                .LogWarning("Chatwoot account sync failed for {UserId}: {Kind}: {Message}", user.Id, e.GetType().Name, e.Message);
        }
    }

    private static async Task<IResult> Synced(HttpContext ctx, PanelResult<PanelUser> r, TimeProvider clock)
    {
        if (r.Outcome == PanelOutcome.Ok)
        {
            await SyncChatwoot(ctx, r.Value!);
        }

        return UserAnswer(r, clock);
    }

    private static IResult UserAnswer(PanelResult<PanelUser> r, TimeProvider clock) =>
        r.Outcome == PanelOutcome.Ok ? Results.Ok(UserDto.From(r.Value!, clock.GetUtcNow())) : Refused(r);

    private static IResult Refused<T>(PanelResult<T> r) => r.Outcome switch
    {
        PanelOutcome.NotFound => Results.NotFound(new { error = "not_found" }),
        PanelOutcome.Forbidden => Results.Json(new { error = r.Error ?? "forbidden" }, statusCode: StatusCodes.Status403Forbidden),
        PanelOutcome.Conflict => Results.Conflict(new { error = r.Error }),
        PanelOutcome.Unavailable => Results.Json(new { error = r.Error ?? "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.BadRequest(new
        {
            error = r.Error ?? "invalid",
            errors = r.Fields?.ToDictionary(f => f.Key, f => new[] { f.Value }),
        }),
    };
}

// ---------- requests ----------

public sealed record LoginRequest(string? Username, string? Password);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record AssignRequest(Guid? StaffId);

public sealed record RoleRequest(string? Role);

public sealed record NewUserRequest(string? Username, string? DisplayName, string? Email, string? Role, string? StaffName);

public sealed record SecondStepRequest(string? Ticket, string? Code);

public sealed record RegisterRequest(string? Username, string? DisplayName, string? Email, string? Password);

public sealed record EmailRequest(string? Email);

public sealed record CodeRequest(string? Email, string? Code);

public sealed record ForgotRequest(string? Login);

public sealed record ResetRequest(string? Login, string? Code, string? NewPassword);

public sealed record OneCodeRequest(string? Code);

public sealed record DisableTwoFactorRequest(string? Password, string? Code);

public sealed record ApproveRequest(string? Role, string? StaffName);

// ---------- responses ----------

public sealed record MeDto(Guid Id, string Username, string DisplayName, string Email, string Role,
    IReadOnlyList<string> Permissions, bool MustChangePassword, bool TwoFactor, bool TwoFactorRequired);

public sealed record StaffDto(Guid Id, string Name);

public sealed record DashboardTaskDto(Guid Id, string Type, string State, DateTimeOffset DueAt, DateTimeOffset CreatedAt,
    string? ClientName, string? StaffName, bool Late);

public sealed record DashboardDto(int Urgent, int Overdue, int Unassigned, int Active, IReadOnlyList<int> UrgentByDay,
    IReadOnlyList<int> OverdueByDay, IReadOnlyList<DashboardTaskDto> Recent, IReadOnlyList<DashboardTaskDto> Priority)
{
    private static DashboardTaskDto Map(DashboardTask t) =>
        new(t.Id, t.Type, t.State, t.DueAt, t.CreatedAt, t.ClientName, t.StaffName, t.Late);

    public static DashboardDto From(Dashboard d) => new(d.Urgent, d.Overdue, d.Unassigned, d.Active, d.UrgentByDay, d.OverdueByDay,
        d.Recent.Select(Map).ToList(), d.Priority.Select(Map).ToList());
}

public sealed record TaskItemDto(Guid Id, string Type, string State, DateTimeOffset DueAt, DateTimeOffset CreatedAt, StaffDto? Staff,
    string? ClientName, string PhoneLast3, string? ClientType, string? MatterType);

public sealed record TaskPageDto(IReadOnlyList<TaskItemDto> Items, int Total, int Page, int PageSize)
{
    public static TaskPageDto From(TaskList l) => new(
        l.Items.Select(t => new TaskItemDto(t.Id, t.Type, t.State, t.DueAt, t.CreatedAt,
            t.Staff is { } s ? new StaffDto(s.Id, s.Name) : null, t.ClientName, t.PhoneLast3, t.ClientType, t.MatterType)).ToList(),
        l.Total, l.Page, l.PageSize);
}

public sealed record ClientDto(string? Name, string Phone, string? ClientType, string? Source, string? ConversationState);

public sealed record ClientWordDto(string Type, string? Text, string? Caption, string? Transcript, string? MediaStatus, DateTimeOffset? SentAt);

public sealed record CanDto(bool Start, bool Done, bool Cancel, bool Assign);

public sealed record TaskDetailDto(Guid Id, string Type, string State, DateTimeOffset DueAt, DateTimeOffset CreatedAt, StaffDto? Staff,
    ClientDto Client, string? MatterType, string? MatterSubtype, IReadOnlyList<ClientWordDto> ClientWords, CanDto Can)
{
    public static TaskDetailDto From(TaskView v)
    {
        var t = v.Task;
        return new(t.Id, t.Type, t.State, t.DueAt, t.CreatedAt, t.Staff is { } s ? new StaffDto(s.Id, s.Name) : null,
            new ClientDto(t.ClientName, t.WaId, t.ClientType, t.Source, t.ConversationState), t.MatterType, t.MatterSubtype,
            t.ClientWords.Select(w => new ClientWordDto(w.Type, w.Text, w.Caption, w.Transcript, w.MediaStatus, w.SentAt)).ToList(),
            new CanDto(v.Can.Start, v.Can.Done, v.Can.Cancel, v.Can.Assign));
    }
}

/// <summary>A pending account (v1.9) has no role yet: <see cref="Role"/> is null until the manager approves it.</summary>
public sealed record UserDto(Guid Id, string Username, string DisplayName, string Email, string? Role, StaffDto? Staff,
    bool Active, bool Locked, bool MustChangePassword, DateTimeOffset? LastLoginAt, bool Pending, bool EmailVerified,
    bool TwoFactor, DateTimeOffset CreatedAt)
{
    public static UserDto From(PanelUser u, DateTimeOffset now) => new(u.Id, u.Username, u.DisplayName, u.Email,
        u.Pending ? null : u.Role, u.StaffId is { } s ? new StaffDto(s, u.StaffName ?? "") : null, u.Active, u.IsLocked(now),
        u.MustChangePassword, u.LastLoginAt, u.Pending, u.EmailVerifiedAt is not null, u.TotpEnabled, u.CreatedAt);
}
