using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Application.Panel;
using Office.Infrastructure.Panel;

namespace Office.IntegrationTests;

/// <summary>
/// DECISIONS v1.9 T-23 through the real API and database: sign-up with an e-mail code, the manager
/// approves or refuses, a forgotten password by code, and the authenticator app as a second step.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class PanelSignupApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Password = "a long test password 1";
    private const string BossEmail = "boss@example.test";
    private readonly MutableClock _clock = new(DateTimeOffset.UtcNow);
    private ApiFactory _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync("TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;");
        _factory = new ApiFactory(db, _clock);
        using var scope = _factory.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<PanelAccounts>().CreateAsync(null,
            new PanelAccounts.NewAccount("boss", "المدير", BossEmail, Permissions.Manager, null), Password, Ct);
        Assert.Equal(PanelOutcome.Ok, r.Outcome);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await db.ExecAsync("TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;");
    }

    // ---------- helpers ----------

    private async Task<HttpClient> Anonymous()
    {
        var c = _factory.CreateClient();
        await Token(c);
        return c;
    }

    private static async Task Token(HttpClient c)
    {
        var body = await c.GetFromJsonAsync<JsonNode>("/api/panel/auth/csrf", Ct);
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (string)body!["token"]!);
    }

    private static Task<HttpResponseMessage> Post(HttpClient c, string path, object body) =>
        c.PostAsJsonAsync("/api/panel" + path, body, Ct);

    private static async Task<JsonNode> Json(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!;

    private static async Task<string?> Error(HttpResponseMessage r) => (string?)(await Json(r))["error"];

    private async Task<HttpClient> SignIn(string user, string password = Password)
    {
        var c = await Anonymous();
        var r = await Post(c, "/auth/login", new { username = user, password });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await Token(c);
        return c;
    }

    private async Task<HttpResponseMessage> Register(HttpClient c, string user, string email, string password = Password) =>
        await Post(c, "/auth/register", new { username = user, display_name = "منى علي", email, password });

    /// <summary>Registered and confirmed: waits for the manager.</summary>
    private async Task<Guid> Confirmed(string user, string email)
    {
        var c = await Anonymous();
        Assert.Equal(HttpStatusCode.Accepted, (await Register(c, user, email)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Post(c, "/auth/register/verify", new { email, code = _factory.Mail.LastCode(email) })).StatusCode);
        return await db.ScalarAsync<Guid>($"SELECT id FROM hazem.panel_users WHERE username = '{user}'");
    }

    private string Totp(string secret, int stepsFromNow = 0) =>
        PanelCrypto.Totp(PanelCrypto.FromBase32(secret), _clock.GetUtcNow().ToUnixTimeSeconds() / 30 + stepsFromNow);

    // ---------- sign-up ----------

    [Fact]
    public async Task Sign_up_confirm_the_email_then_the_manager_approves_with_a_role()
    {
        var c = await Anonymous();
        Assert.True((bool)(await c.GetFromJsonAsync<JsonNode>("/api/panel/auth/signup", Ct))!["available"]!);

        var r = await Register(c, "mona", "mona@example.test");
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        Assert.True((bool)(await Json(r))["mail_sent"]!);
        var mail = Assert.Single(_factory.Mail.To("mona@example.test"));
        Assert.Contains("كود تأكيد", mail.Subject);
        Assert.Matches(@"^\d{6}$", _factory.Mail.LastCode("mona@example.test"));
        Assert.Contains("dir=\"rtl\"", mail.Html);

        // Not in yet, and only the right password learns why.
        var login = await Post(c, "/auth/login", new { username = "mona", password = "a wrong password 12" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        login = await Post(c, "/auth/login", new { username = "mona", password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("email_not_verified", await Error(login));

        Assert.Equal("invalid_code", await Error(await Post(c, "/auth/register/verify", new { email = "mona@example.test", code = "000000" })));
        Assert.Equal(HttpStatusCode.NoContent, (await Post(c, "/auth/register/verify",
            new { email = "mona@example.test", code = _factory.Mail.LastCode("mona@example.test") })).StatusCode);
        Assert.Contains(_factory.Mail.To(BossEmail), m => m.Subject.Contains("مستني التفعيل") && m.Text.Contains("mona"));

        login = await Post(c, "/auth/login", new { username = "mona", password = Password });
        Assert.Equal("pending_approval", await Error(login));

        // The manager sees it waiting, with no role yet.
        var boss = await SignIn("boss");
        var users = (await boss.GetFromJsonAsync<JsonArray>("/api/panel/users", Ct))!;
        var pending = users[0]!;
        Assert.Equal("mona", (string)pending["username"]!);
        Assert.True((bool)pending["pending"]!);
        Assert.True((bool)pending["email_verified"]!);
        Assert.Null(pending["role"]);
        var id = (Guid)pending["id"]!;
        Assert.Equal(HttpStatusCode.Conflict, (await Post(boss, $"/users/{id}/enable", new { })).StatusCode);

        var approved = await Post(boss, $"/users/{id}/approve", new { role = "staff", staff_name = "منى" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var u = await Json(approved);
        Assert.Equal("staff", (string)u["role"]!);
        Assert.Equal("منى", (string)u["staff"]!["name"]!);
        Assert.False((bool)u["pending"]!);
        Assert.Contains(_factory.Mail.To("mona@example.test"), m => m.Subject.Contains("اتفعّل"));

        var mona = await SignIn("mona");
        var me = (await mona.GetFromJsonAsync<JsonNode>("/api/panel/auth/me", Ct))!;
        Assert.False((bool)me["must_change_password"]!); // the person chose the password
        Assert.Equal(1, await db.ScalarAsync<long>(
            $"SELECT count(*) FROM hazem.panel_audit WHERE action = 'user.approve' AND target_id = '{id}'"));
    }

    [Fact]
    public async Task A_taken_email_answers_the_same_and_warns_its_owner_a_taken_name_is_said()
    {
        var c = await Anonymous();
        var r = await Register(c, "someone", BossEmail.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        Assert.Equal(0, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_users WHERE username = 'someone'"));
        var warning = Assert.Single(_factory.Mail.To(BossEmail));
        Assert.Contains("محاولة تسجيل", warning.Subject);
        Assert.DoesNotMatch(@"(?m)^\d{6}$", warning.Text);

        r = await Register(c, "boss", "other@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("username_taken", (string)(await Json(r))["errors"]!["username"]![0]!);

        r = await Register(c, "x", "not-an-email", "short");
        var errors = (await Json(r))["errors"]!.AsObject();
        Assert.Equal(["email", "password", "username"], errors.Select(e => e.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_refused_registration_is_deleted_and_its_name_is_free_again()
    {
        var id = await Confirmed("karim", "karim@example.test");
        var boss = await SignIn("boss");
        Assert.Equal(HttpStatusCode.NoContent, (await Post(boss, $"/users/{id}/reject", new { })).StatusCode);
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.panel_users WHERE id = '{id}'"));
        Assert.Contains(_factory.Mail.To("karim@example.test"), m => m.Subject.Contains("ما اتقبلش"));
        Assert.Equal(HttpStatusCode.Conflict, (await Post(boss, $"/users/{await db.ScalarAsync<Guid>("SELECT id FROM hazem.panel_users WHERE username = 'boss'")}/reject", new { })).StatusCode);

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Accepted, (await Register(await Anonymous(), "karim", "karim@example.test")).StatusCode);
    }

    [Fact]
    public async Task A_code_dies_after_five_wrong_tries_or_ten_minutes_and_resending_waits_a_minute()
    {
        var c = await Anonymous();
        await Register(c, "sara", "sara@example.test");
        var good = _factory.Mail.LastCode("sara@example.test");
        var wrong = good == "123456" ? "654321" : "123456";
        for (var i = 0; i < PanelSignup.MaxCodeAttempts; i++)
        {
            Assert.Equal("invalid_code", await Error(await Post(c, "/auth/register/verify", new { email = "sara@example.test", code = wrong })));
        }

        Assert.Equal("invalid_code", await Error(await Post(c, "/auth/register/verify", new { email = "sara@example.test", code = good })));

        Assert.Equal(HttpStatusCode.Accepted, (await Post(c, "/auth/register/resend", new { email = "sara@example.test" })).StatusCode);
        Assert.Single(_factory.Mail.To("sara@example.test")); // too soon: nothing sent
        _clock.Advance(TimeSpan.FromSeconds(61));
        await Post(c, "/auth/register/resend", new { email = "sara@example.test" });
        Assert.Equal(2, _factory.Mail.To("sara@example.test").Count);

        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal("invalid_code", await Error(await Post(c, "/auth/register/verify",
            new { email = "sara@example.test", code = _factory.Mail.LastCode("sara@example.test") })));

        // An address nobody registered: the same quiet answer, no mail.
        Assert.Equal(HttpStatusCode.Accepted, (await Post(c, "/auth/register/resend", new { email = "nobody@example.test" })).StatusCode);
        Assert.Empty(_factory.Mail.To("nobody@example.test"));
    }

    [Fact]
    public async Task Without_the_key_and_the_mail_server_sign_up_says_it_is_not_available()
    {
        await using var bare = new ApiFactory(db) { SignupConfigured = false };
        var c = bare.CreateClient();
        await Token(c);
        Assert.False((bool)(await c.GetFromJsonAsync<JsonNode>("/api/panel/auth/signup", Ct))!["available"]!);
        var r = await Post(c, "/auth/register", new { username = "mona", display_name = "منى", email = "m@example.test", password = Password });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("not_configured", await Error(r));
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/auth/login", new { username = "boss", password = Password })).StatusCode);
    }

    // ---------- a forgotten password ----------

    [Fact]
    public async Task A_forgotten_password_is_changed_with_the_emailed_code_and_old_sessions_end()
    {
        var old = await SignIn("boss");
        var c = await Anonymous();
        Assert.Equal(HttpStatusCode.Accepted, (await Post(c, "/auth/forgot", new { login = "nobody" })).StatusCode);
        Assert.Empty(_factory.Mail.Sent);

        Assert.Equal(HttpStatusCode.Accepted, (await Post(c, "/auth/forgot", new { login = "BOSS" })).StatusCode);
        var code = _factory.Mail.LastCode(BossEmail);
        Assert.Contains("تغيير كلمة السر", _factory.Mail.To(BossEmail)[^1].Subject);

        var weak = await Post(c, "/auth/reset", new { login = "boss", code, new_password = "short" });
        Assert.Equal("weak_password", await Error(weak));
        Assert.Equal("invalid_code", await Error(await Post(c, "/auth/reset", new { login = "boss", code = "000000", new_password = "a brand new password 2" })));
        Assert.Equal(HttpStatusCode.NoContent,
            (await Post(c, "/auth/reset", new { login = BossEmail, code, new_password = "a brand new password 2" })).StatusCode);
        Assert.Equal("invalid_code", await Error(await Post(c, "/auth/reset", new { login = "boss", code, new_password = "a third password 33" })));

        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/panel/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(c, "/auth/login", new { username = "boss", password = Password })).StatusCode);
        await SignIn("boss", "a brand new password 2");
        Assert.Contains(_factory.Mail.To(BossEmail), m => m.Subject.Contains("كلمة السر اتغيرت"));
    }

    // ---------- the authenticator ----------

    [Fact]
    public async Task The_authenticator_makes_sign_in_a_second_step_and_a_code_works_once()
    {
        var boss = await SignIn("boss");
        var setup = await Json(await Post(boss, "/auth/2fa/setup", new { }));
        var secret = (string)setup["secret"]!;
        Assert.StartsWith("otpauth://totp/", (string)setup["uri"]!);
        Assert.Equal("invalid_code", await Error(await Post(boss, "/auth/2fa/enable", new { code = "000000" })));

        var on = await Post(boss, "/auth/2fa/enable", new { code = Totp(secret, -1) });
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        var recovery = (await Json(on))["recovery_codes"]!.AsArray().Select(x => (string)x!).ToList();
        Assert.Equal(10, recovery.Count);
        Assert.All(recovery, r => Assert.Matches("^[a-z2-9]{4}-[a-z2-9]{4}$", r));
        Assert.Equal(HttpStatusCode.OK, (await boss.GetAsync("/api/panel/auth/me", Ct)).StatusCode); // this session goes on
        var stored = await db.ScalarAsync<string>("SELECT totp_secret FROM hazem.panel_users WHERE username = 'boss'");
        Assert.DoesNotContain(secret, stored); // encrypted at rest

        // The password alone opens nothing now.
        var c = await Anonymous();
        var first = await Json(await Post(c, "/auth/login", new { username = "boss", password = Password }));
        Assert.True((bool)first["two_factor"]!);
        var ticket = (string)first["ticket"]!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/panel/auth/me", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(c, "/auth/login/2fa", new { ticket, code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(c, "/auth/login/2fa", new { ticket = "forged", code = Totp(secret) })).StatusCode);
        var second = await Post(c, "/auth/login/2fa", new { ticket, code = Totp(secret) });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True((bool)(await Json(second))["two_factor"]!);

        // The same code again (seen over a shoulder) is spent; a recovery code works once.
        var again = await Anonymous();
        ticket = (string)(await Json(await Post(again, "/auth/login", new { username = "boss", password = Password })))["ticket"]!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(again, "/auth/login/2fa", new { ticket, code = Totp(secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(again, "/auth/login/2fa", new { ticket, code = recovery[0].ToUpperInvariant() })).StatusCode);
        var third = await Anonymous();
        ticket = (string)(await Json(await Post(third, "/auth/login", new { username = "boss", password = Password })))["ticket"]!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(third, "/auth/login/2fa", new { ticket, code = recovery[0] })).StatusCode);

        // An old ticket dies after five minutes.
        _clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(third, "/auth/login/2fa", new { ticket, code = Totp(secret, 1) })).StatusCode);

        var status = (await boss.GetFromJsonAsync<JsonNode>("/api/panel/auth/2fa", Ct))!;
        Assert.True((bool)status["enabled"]!);
        Assert.Equal(9, (int)status["recovery_codes_left"]!);
        Assert.Contains(_factory.Mail.To(BossEmail), m => m.Subject.Contains("اتشغّل"));
    }

    [Fact]
    public async Task Turning_it_off_needs_the_password_and_a_code_and_the_manager_can_reset_someone_else()
    {
        var id = await Confirmed("mona", "mona@example.test");
        var boss = await SignIn("boss");
        await Post(boss, $"/users/{id}/approve", new { role = "lawyer" });
        var mona = await SignIn("mona");
        var secret = (string)(await Json(await Post(mona, "/auth/2fa/setup", new { })))["secret"]!;
        Assert.Equal(HttpStatusCode.OK, (await Post(mona, "/auth/2fa/enable", new { code = Totp(secret, -1) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(mona, "/auth/2fa/setup", new { })).StatusCode);

        Assert.Equal("wrong_current_password", await Error(await Post(mona, "/auth/2fa/disable", new { password = "not it at all 123", code = Totp(secret) })));
        Assert.Equal("invalid_code", await Error(await Post(mona, "/auth/2fa/disable", new { password = Password, code = "000000" })));

        // A lost phone: the manager turns it off; the person's sessions end.
        var users = (await boss.GetFromJsonAsync<JsonArray>("/api/panel/users", Ct))!;
        Assert.True((bool)users.Single(u => (string)u!["username"]! == "mona")!["two_factor"]!);
        Assert.Equal(HttpStatusCode.OK, (await Post(boss, $"/users/{id}/reset-2fa", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mona.GetAsync("/api/panel/auth/me", Ct)).StatusCode);
        mona = await SignIn("mona"); // the password alone again
        Assert.False((bool)(await mona.GetFromJsonAsync<JsonNode>("/api/panel/auth/2fa", Ct))!["enabled"]!);
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.panel_recovery_codes WHERE user_id = '{id}'"));

        // And by the person, with both.
        secret = (string)(await Json(await Post(mona, "/auth/2fa/setup", new { })))["secret"]!;
        await Post(mona, "/auth/2fa/enable", new { code = Totp(secret, -1) });
        Assert.Equal(HttpStatusCode.NoContent, (await Post(mona, "/auth/2fa/disable", new { password = Password, code = Totp(secret) })).StatusCode);
    }

    [Fact]
    public async Task A_manager_must_turn_the_authenticator_on_before_anything_else()
    {
        await using var strict = new ApiFactory(db, _clock) { ManagersNeedTwoFactor = true };
        var boss = strict.CreateClient();
        await Token(boss);
        var login = await Post(boss, "/auth/login", new { username = "boss", password = Password });
        Assert.True((bool)(await Json(login))["two_factor_required"]!);
        await Token(boss);

        var refused = await boss.GetAsync("/api/panel/users", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("two_factor_required", await Error(refused));
        Assert.Equal(HttpStatusCode.OK, (await boss.GetAsync("/api/panel/auth/me", Ct)).StatusCode);

        var secret = (string)(await Json(await Post(boss, "/auth/2fa/setup", new { })))["secret"]!;
        Assert.Equal(HttpStatusCode.OK, (await Post(boss, "/auth/2fa/enable", new { code = Totp(secret, -1) })).StatusCode);
        Assert.False((bool)(await boss.GetFromJsonAsync<JsonNode>("/api/panel/auth/me", Ct))!["two_factor_required"]!);
        Assert.Equal(HttpStatusCode.OK, (await boss.GetAsync("/api/panel/users", Ct)).StatusCode);

        // Others are free to choose.
        var id = await Confirmed("mona", "mona@example.test");
        await Post(boss, $"/users/{id}/approve", new { role = "staff" });
        var mona = strict.CreateClient();
        await Token(mona);
        Assert.False((bool)(await Json(await Post(mona, "/auth/login", new { username = "mona", password = Password })))["two_factor_required"]!);
        await Token(mona);
        Assert.Equal(HttpStatusCode.OK, (await mona.GetAsync("/api/panel/tasks", Ct)).StatusCode);
    }

    [Fact]
    public void Totp_matches_rfc_6238()
    {
        // RFC 6238 appendix B, SHA-1, the last six of the eight digits.
        var key = "12345678901234567890"u8.ToArray();
        Assert.Equal("287082", PanelCrypto.Totp(key, 59 / 30));
        Assert.Equal("081804", PanelCrypto.Totp(key, 1111111109 / 30));
        Assert.Equal(key, PanelCrypto.FromBase32(PanelCrypto.ToBase32(key)));
    }
}

/// <summary>Now, until a test moves it: the e-mail code and the authenticator both count time.</summary>
public sealed class MutableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
