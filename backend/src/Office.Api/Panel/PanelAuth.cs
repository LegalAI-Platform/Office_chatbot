using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Office.Application.Panel;

namespace Office.Api.Panel;

/// <summary>
/// Sign-in for the panel (DECISIONS v1.8 T-23): a cookie session, a permission policy per
/// permission (never a role check), antiforgery on every change, and a login rate limit.
/// </summary>
public static class PanelAuth
{
    public const string Scheme = "panel";
    public const string CookieName = "hazem_panel";
    public const string CsrfHeader = "X-CSRF-TOKEN";
    public const string StampClaim = "hazem:stamp";
    public const string LoginLimiter = "panel-login";
    public const string SensitiveLimiter = "panel-sensitive";
    public const string StartedClaim = "hazem:started";
    public const string AntiforgeryFailedHeader = "X-Antiforgery-Failed";

    /// <summary>The account behind the request, loaded and checked once per request.</summary>
    public const string UserItem = "panel.user";

    /// <summary>An hour without a request ends the session (the panel also signs out after an hour without use).</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(1);

    /// <summary>However active, a session ends after this long from its sign-in (a copied cookie does not live forever).</summary>
    public static readonly TimeSpan MaxSessionAge = TimeSpan.FromHours(12);

    /// <summary>How long the right password waits for the authenticator code.</summary>
    public static readonly TimeSpan SecondStepFor = TimeSpan.FromMinutes(5);

    public static string Policy(string permission) => "perm:" + permission;

    public static IServiceCollection AddPanelAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // Plain http is allowed only on the test machine (127.0.0.1); everywhere else HTTPS only.
        var secure = env.IsEnvironment("test") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        // The keys that sign the session cookie, the antiforgery token and the 2FA ticket. Kept on a
        // volume (Panel:KeysPath): inside the container they were new after every rebuild, which
        // signed everyone out. No path (the tests) = the default per-process keys.
        var keys = services.AddDataProtection().SetApplicationName("hazem-panel");
        if (config["Panel:KeysPath"] is { Length: > 0 } keysPath)
        {
            keys.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services.AddAuthentication(Scheme).AddCookie(Scheme, o =>
        {
            o.Cookie.Name = CookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = secure;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = IdleTimeout;
            o.SlidingExpiration = true;
            // An API: answer 401/403, never redirect to a login page.
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            // Every request re-reads the account: disabled, a new password or a new role (a new
            // security stamp) ends every session at once.
            o.Events.OnValidatePrincipal = async ctx =>
            {
                var accounts = ctx.HttpContext.RequestServices.GetRequiredService<PanelAccounts>();
                var now = ctx.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                var fresh = long.TryParse(ctx.Principal?.FindFirstValue(StartedClaim), out var started)
                    && now - DateTimeOffset.FromUnixTimeSeconds(started) < MaxSessionAge;
                var user = fresh && Guid.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                    ? await accounts.ValidateSessionAsync(id, ctx.Principal!.FindFirstValue(StampClaim), ctx.HttpContext.RequestAborted)
                    : null;
                if (user is null)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(Scheme);
                    return;
                }

                ctx.HttpContext.Items[UserItem] = user;
            };
        });

        services.AddAuthorization(o =>
        {
            foreach (var permission in Permissions.All)
            {
                o.AddPolicy(Policy(permission), p => p
                    .AddAuthenticationSchemes(Scheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(ctx => ctx.User.FindFirstValue(ClaimTypes.Role) is { } role && Permissions.Has(role, permission)));
            }
        });

        services.AddAntiforgery(o =>
        {
            o.HeaderName = CsrfHeader;
            o.Cookie.Name = "hazem_af";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = secure;
            o.SuppressXFrameOptionsHeader = false;
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // At most 10 sign-in attempts a minute from one address (on top of the account lockout). The
            // same budget covers sign-up, the e-mail codes and the second step: they share the partition.
            // Panel:LoginsPerMinute exists for the tests, which sign in many times in a minute.
            var perMinute = config.GetValue("Panel:LoginsPerMinute", 10);
            o.AddPolicy(LoginLimiter, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // What proves the person again inside a session (the current password, an authenticator code): ten
            // tries in 15 minutes per account, so a session left open cannot be used to guess either.
            var sensitive = config.GetValue("Panel:SensitivePer15Minutes", 10);
            o.AddPolicy(SensitiveLimiter, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = sensitive, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
        });

        // The panel reaches the API through nginx in the web container (office_net only; the API
        // has no host port), so the client address comes from X-Forwarded-For.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        return services;
    }

    public static ClaimsPrincipal Principal(PanelUser user, DateTimeOffset started) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim(StampClaim, user.SecurityStamp),
        new Claim(StartedClaim, started.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
    ], Scheme));

    public static Task SignInAsync(HttpContext ctx, PanelUser user) =>
        ctx.SignInAsync(Scheme, Principal(user, ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });

    /// <summary>
    /// v1.9 (T-23): an account that manages accounts (the manager) must have the authenticator on.
    /// Only once PANEL_SECRET_KEY is set (it cannot be set up without it); Panel:ManagersNeedTwoFactor
    /// (default true) turns the rule off for tests that are not about it.
    /// </summary>
    public static bool NeedsTwoFactor(HttpContext ctx, PanelUser user) =>
        !user.TotpEnabled
        && Permissions.Has(user.Role, Permissions.UsersManage)
        && ctx.RequestServices.GetRequiredService<IPanelCrypto>().Configured
        && ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Panel:ManagersNeedTwoFactor", true);

    /// <summary>The account checked by the cookie handler for this request.</summary>
    public static PanelUser CurrentUser(this HttpContext ctx) =>
        ctx.Items[UserItem] as PanelUser ?? throw new InvalidOperationException("No panel account on this request.");
}

/// <summary>Antiforgery on every request that changes something (the panel sends X-CSRF-TOKEN).</summary>
public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                http.Response.Headers[PanelAuth.AntiforgeryFailedHeader] = "1";
                return Results.BadRequest(new { error = "antiforgery" });
            }
        }

        return await next(context);
    }
}

/// <summary>
/// A temporary password must be changed first: everything except me, change-password, logout
/// and csrf answers 403 must_change_password until then.
/// </summary>
public sealed class MustChangePasswordFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Items[PanelAuth.UserItem] is PanelUser { MustChangePassword: true }
            ? Results.Json(new { error = "must_change_password" }, statusCode: StatusCodes.Status403Forbidden)
            : await next(context);
}

/// <summary>
/// A manager without the authenticator (v1.9): everything except me, logout, csrf, change-password
/// and /auth/2fa answers 403 two_factor_required until it is on.
/// </summary>
public sealed class TwoFactorRequiredFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Items[PanelAuth.UserItem] is PanelUser user && PanelAuth.NeedsTwoFactor(context.HttpContext, user)
            ? Results.Json(new { error = "two_factor_required" }, statusCode: StatusCodes.Status403Forbidden)
            : await next(context);
}

/// <summary>Client data never lands in a browser or proxy cache.</summary>
public sealed class NoStoreFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        return await next(context);
    }
}
