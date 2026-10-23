using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Office.Application.Panel;

namespace Office.IntegrationTests;

/// <summary>TASK 07a through the real API and database: sign-in, RBAC, accounts and tasks (T-23).</summary>
[Collection(nameof(HazemApiTests))]
public sealed class PanelApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Password = "a long test password 1";
    private ApiFactory _factory = null!;
    private Guid _staffA;
    private Guid _staffB;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync("""
            TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
            TRUNCATE hazem.turns, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
            DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
            """);
        _factory = new ApiFactory(db);
        await NewUser("boss", Permissions.Manager);
        await NewUser("lawyer", Permissions.Lawyer);
        _staffA = (await NewUser("yasmin", Permissions.Staff, staff: "ياسمين")).StaffId!.Value;
        _staffB = (await NewUser("karim", Permissions.Staff, staff: "كريم")).StaffId!.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await db.ExecAsync("TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;"); // they point at hazem.staff
    }

    // ---------- helpers ----------

    private async Task<PanelUser> NewUser(string name, string role, string? staff = null, bool temporary = false)
    {
        using var scope = _factory.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<PanelAccounts>().CreateAsync(null,
            new PanelAccounts.NewAccount(name, name, $"{name}@example.test", role, staff), temporary ? null : Password, Ct);
        Assert.Equal(PanelOutcome.Ok, r.Outcome);
        return r.Value.User;
    }

    private static async Task<string> Csrf(HttpClient c)
    {
        var body = await c.GetFromJsonAsync<JsonNode>("/api/panel/auth/csrf", Ct);
        return (string)body!["token"]!;
    }

    private static async Task Token(HttpClient c)
    {
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await Csrf(c));
    }

    private static Task<HttpResponseMessage> Login(HttpClient c, string user, string password) =>
        c.PostAsJsonAsync("/api/panel/auth/login", new { username = user, password }, Ct);

    private async Task<HttpClient> SignIn(string user, string password = Password)
    {
        var c = _factory.CreateClient();
        await Token(c);
        var r = await Login(c, user, password);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await Token(c); // the token belongs to the signed-in user now
        return c;
    }

    private async Task<Guid> NewTask(string type = "human_requested", Guid? staff = null, string state = "open", string due = "1 hour")
    {
        var person = Guid.NewGuid();
        var turn = "wt:" + Guid.NewGuid();
        var wa = "2010" + Random.Shared.Next(10_000_000, 99_999_999);
        var staffSql = staff is null ? "NULL" : $"'{staff}'";
        await db.ExecAsync($$$"""
            INSERT INTO hazem.persons (id, wa_id, name_raw, name_normalized, full_name_provided) VALUES ('{{{person}}}', '{{{wa}}}', 'سارة محمد علي حسن', 'سارة محمد علي حسن', true);
            INSERT INTO hazem.turns (turn_id, wa_id, messages, status, responder) VALUES ('{{{turn}}}', '{{{wa}}}', '[
              {"wamid":"w1","type":"text","text":"عايزة أكلم حد من المكتب","sent_at":"2026-10-22T09:00:00+03:00"},
              {"wamid":"w2","type":"image","caption":"صورة العقد","media":{"status":"stored","bucket":"office-sensitive","key":"wa/2026/10/secret-key.jpg","filename":"عقد سارة.jpg","sha256":"abc"}},
              {"wamid":"w3","type":"audio","media":{"status":"stored","bucket":"office-media","key":"wa/2026/10/v.ogg"},"transcript":{"status":"done","text":"الجلسة بكرة"}}
            ]'::jsonb, 'processed', 'hazem');
            INSERT INTO hazem.staff_tasks (id, person_id, turn_id, type, state, staff_id, due_at)
            VALUES ('{{{person}}}', '{{{person}}}', '{{{turn}}}', '{{{type}}}', '{{{state}}}', {{{staffSql}}}, now() + interval '{{{due}}}');
            """);
        return person; // the task id equals the person id in these tests
    }

    // ---------- sign-in ----------

    [Fact]
    public async Task Wrong_password_unknown_user_disabled_and_locked_all_answer_the_same()
    {
        var c = _factory.CreateClient();
        await Token(c);
        var unknown = await Login(c, "nobody", Password);
        var wrong = await Login(c, "yasmin", "not the password at all");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(Ct), await wrong.Content.ReadAsStringAsync(Ct));

        for (var i = 0; i < 4; i++)
        {
            await Login(c, "yasmin", "not the password at all");
        }

        var locked = await Login(c, "yasmin", Password); // right password, locked account
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(Ct), await locked.Content.ReadAsStringAsync(Ct));
        Assert.True(await db.ScalarAsync<bool>("SELECT lockout_end > now() + interval '14 minutes' FROM hazem.panel_users WHERE username = 'yasmin'"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_audit WHERE action = 'login.locked'"));
    }

    [Fact]
    public async Task The_login_rate_limit_answers_429()
    {
        await using var strict = new ApiFactory(db) { LoginsPerMinute = 3 };
        var c = strict.CreateClient();
        await Token(c);
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            codes.Add((await Login(c, "nobody", "x")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, codes[^1]);
    }

    [Fact]
    public async Task The_session_cookie_is_httponly_and_samesite_strict()
    {
        var c = _factory.CreateClient(new() { HandleCookies = false });
        var csrf = await c.GetAsync("/api/panel/auth/csrf", Ct);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/panel/auth/login") { Content = JsonContent.Create(new { username = "boss", password = Password }) };
        request.Headers.Add("X-CSRF-TOKEN", (string)(await csrf.Content.ReadFromJsonAsync<JsonNode>(Ct))!["token"]!);
        request.Headers.Add("Cookie", string.Join("; ", csrf.Headers.GetValues("Set-Cookie").Select(v => v.Split(';')[0])));
        var r = await c.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cookie = r.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("hazem_panel=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.True(r.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task A_change_without_the_antiforgery_token_is_refused()
    {
        var c = await SignIn("yasmin");
        var task = await NewTask();
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var r = await c.PostAsync($"/api/panel/tasks/{task}/start", null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("1", r.Headers.GetValues("X-Antiforgery-Failed").Single());
        Assert.Equal("open", await db.ScalarAsync<string>($"SELECT state FROM hazem.staff_tasks WHERE id = '{task}'"));
    }

    // ---------- RBAC: every endpoint ----------

    [Fact]
    public void Every_panel_endpoint_names_a_permission_policy_except_csrf_sign_in_and_sign_up()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/panel", StringComparison.Ordinal) == true).ToList();
        Assert.True(endpoints.Count >= 15);
        var open = endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null).Select(e => e.RoutePattern.RawText).ToList();
        Assert.Equal([
            "/api/panel/auth/csrf", "/api/panel/auth/forgot", "/api/panel/auth/login", "/api/panel/auth/login/2fa",
            "/api/panel/auth/register", "/api/panel/auth/register/resend", "/api/panel/auth/register/verify",
            "/api/panel/auth/reset", "/api/panel/auth/signup",
        ], open.Order(StringComparer.Ordinal));
        // Every anonymous endpoint but the token is rate-limited (sign-in, sign-up and the codes share it).
        Assert.All(endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null && e.RoutePattern.RawText != "/api/panel/auth/csrf"),
            e => Assert.NotNull(e.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()));
        foreach (var e in endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            Assert.Contains(policies, p => p?.StartsWith("perm:", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(e.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Roles is not null);
        }
    }

    [Theory]
    [InlineData("GET", "/api/panel/auth/me")]
    [InlineData("GET", "/api/panel/tasks")]
    [InlineData("GET", "/api/panel/tasks/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/panel/staff")]
    [InlineData("GET", "/api/panel/users")]
    public async Task Without_a_session_every_endpoint_answers_401(string method, string path)
    {
        var r = await _factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/panel/users")]
    [InlineData("POST", "/api/panel/users")]
    [InlineData("GET", "/api/panel/staff")]
    [InlineData("GET", "/api/panel/tasks?scope=all")]
    public async Task A_staff_member_is_refused_what_needs_another_permission(string method, string path)
    {
        var c = await SignIn("yasmin");
        var r = await c.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = method == "POST" ? JsonContent.Create(new { }) : null }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Me_lists_the_permissions_of_the_role_only()
    {
        var c = await SignIn("lawyer");
        var me = (await c.GetFromJsonAsync<JsonNode>("/api/panel/auth/me", Ct))!;
        Assert.Equal("lawyer", (string?)me["role"]);
        var permissions = me["permissions"]!.AsArray().Select(p => (string)p!).ToList();
        Assert.Contains("tasks.assign", permissions);
        Assert.DoesNotContain("users.manage", permissions);
    }

    // ---------- passwords and sessions ----------

    [Fact]
    public async Task A_temporary_password_must_be_changed_first_and_the_change_ends_other_sessions()
    {
        var created = await NewUser("newbie", Permissions.Staff, staff: "جديد", temporary: true);
        Assert.True(created.MustChangePassword);
        using var scope = _factory.Services.CreateScope();
        var temp = (await scope.ServiceProvider.GetRequiredService<PanelAccounts>().ResetPasswordAsync(null, created.Id, null, Ct)).Value!;

        var first = await SignIn("newbie", temp);
        var second = await SignIn("newbie", temp);
        var blocked = await first.GetAsync("/api/panel/tasks", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("must_change_password", (string?)(await blocked.Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]);

        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/panel/auth/change-password",
            new { current_password = temp, new_password = "short" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsJsonAsync("/api/panel/auth/change-password",
            new { current_password = temp, new_password = "a brand new long password" }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/panel/tasks", Ct)).StatusCode); // this session goes on
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/panel/auth/me", Ct)).StatusCode); // the other ended
    }

    [Fact]
    public async Task Disabling_an_account_ends_its_sessions_at_once()
    {
        var staff = await SignIn("karim");
        var boss = await SignIn("boss");
        var id = await db.ScalarAsync<Guid>("SELECT id FROM hazem.panel_users WHERE username = 'karim'");
        Assert.Equal(HttpStatusCode.OK, (await boss.PostAsync($"/api/panel/users/{id}/disable", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/panel/auth/me", Ct)).StatusCode);
        var c = _factory.CreateClient();
        await Token(c);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "karim", Password)).StatusCode);
    }

    // ---------- accounts ----------

    [Fact]
    public async Task The_manager_adds_an_account_and_sees_its_temporary_password_once()
    {
        var boss = await SignIn("boss");
        var r = await boss.PostAsJsonAsync("/api/panel/users",
            new { username = "Mona", display_name = "منى", email = "mona@example.test", role = "staff", staff_name = "أ. منى" }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        var temp = (string)body["temporary_password"]!;
        Assert.Matches("^[a-z2-9]{4}-[a-z2-9]{4}-[a-z2-9]{4}-[a-z2-9]{4}$", temp);
        Assert.Equal("mona", (string?)body["user"]!["username"]);
        Assert.Equal("أ. منى", (string?)body["user"]!["staff"]!["name"]);

        var list = await boss.GetStringAsync("/api/panel/users", Ct);
        Assert.DoesNotContain(temp, list);
        Assert.DoesNotContain("password_hash", list);
        Assert.DoesNotContain("AQAAAA", list); // an Identity hash
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_audit WHERE action = 'user.create' AND user_id IS NOT NULL"));

        var dup = await boss.PostAsJsonAsync("/api/panel/users",
            new { username = "mona", display_name = "x", email = "MONA@example.test", role = "staff" }, Ct);
        var errors = (await dup.Content.ReadFromJsonAsync<JsonNode>(Ct))!["errors"]!;
        Assert.Equal("username_taken", (string?)errors["username"]![0]);
        Assert.Equal("email_taken", (string?)errors["email"]![0]);
    }

    [Fact]
    public async Task The_last_manager_can_neither_be_disabled_nor_demoted_and_nobody_changes_themself()
    {
        var boss = await SignIn("boss");
        var bossId = await db.ScalarAsync<Guid>("SELECT id FROM hazem.panel_users WHERE username = 'boss'");
        Assert.Equal(HttpStatusCode.Conflict, (await boss.PostAsync($"/api/panel/users/{bossId}/disable", null, Ct)).StatusCode);

        var second = await NewUser("boss2", Permissions.Manager);
        var other = await SignIn("boss2");
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync($"/api/panel/users/{bossId}/role", new { role = "lawyer" }, Ct)).StatusCode);
        var last = await boss.GetAsync("/api/panel/auth/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, last.StatusCode); // the new role ended the old session
        var r = await (await SignIn("boss")).PostAsJsonAsync($"/api/panel/users/{second.Id}/role", new { role = "staff" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); // a lawyer has no users.manage
        var demoteLast = await other.PostAsJsonAsync($"/api/panel/users/{second.Id}/role", new { role = "lawyer" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, demoteLast.StatusCode);
        Assert.Equal("cannot_change_self", (string?)(await demoteLast.Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]);
    }

    // ---------- tasks ----------
    [Fact]
    public async Task The_home_screen_counts_urgent_and_late_tasks_the_person_may_see()
    {
        var late = await NewTask(staff: _staffA, due: "-2 hours");
        var urgent = await NewTask(type: "urgent_request");
        await NewTask(type: "safety_alert", staff: _staffB, due: "-1 hour");
        await NewTask(state: "done", due: "-3 hours");

        var lawyer = (await (await SignIn("lawyer")).GetFromJsonAsync<JsonNode>("/api/panel/dashboard", Ct))!;
        Assert.Equal(2, (int)lawyer["urgent"]!);
        Assert.Equal(2, (int)lawyer["overdue"]!);
        Assert.Equal(3, (int)lawyer["active"]!);
        Assert.Equal(7, lawyer["urgent_by_day"]!.AsArray().Count);
        Assert.Equal(2, lawyer["urgent_by_day"]!.AsArray().Sum(n => (int)n!));
        Assert.True((bool)lawyer["priority"]![0]!["late"]!);
        Assert.Equal(4, lawyer["recent"]!.AsArray().Count);

        var yasmin = (await (await SignIn("yasmin")).GetFromJsonAsync<JsonNode>("/api/panel/dashboard", Ct))!;
        Assert.Equal(1, (int)yasmin["urgent"]!);
        Assert.Equal(1, (int)yasmin["overdue"]!);
        Assert.Equal([late.ToString(), urgent.ToString()],
            yasmin["priority"]!.AsArray().Select(t => (string?)t!["id"]));
    }

    [Fact]
    public async Task Tasks_are_found_by_part_of_the_name_or_the_last_digits()
    {
        var id = await NewTask();
        var wa = await db.ScalarAsync<string>($"SELECT wa_id FROM hazem.persons WHERE id = '{id}'");
        await NewTask();
        var c = await SignIn("lawyer");

        var byName = (await c.GetFromJsonAsync<JsonNode>("/api/panel/tasks?scope=all&q=" + Uri.EscapeDataString("محمد علي"), Ct))!;
        Assert.Equal(2, (int)byName["total"]!);
        var byDigits = (await c.GetFromJsonAsync<JsonNode>($"/api/panel/tasks?scope=all&q={wa![^5..]}", Ct))!;
        Assert.Equal(id.ToString(), (string?)byDigits["items"]![0]!["id"]);
        Assert.Equal(1, (int)byDigits["total"]!);
        var none = (await c.GetFromJsonAsync<JsonNode>("/api/panel/tasks?scope=all&q=" + Uri.EscapeDataString("مش موجود"), Ct))!;
        Assert.Equal(0, (int)none["total"]!);
    }


    [Fact]
    public async Task A_staff_member_sees_own_and_unassigned_tasks_and_never_learns_about_others()
    {
        var mine = await NewTask(staff: _staffA);
        var free = await NewTask();
        var theirs = await NewTask(staff: _staffB);
        var c = await SignIn("yasmin");

        var ids = new List<string?>();
        foreach (var scope in new[] { "mine", "unassigned" })
        {
            var page = (await c.GetFromJsonAsync<JsonNode>($"/api/panel/tasks?scope={scope}", Ct))!;
            ids.AddRange(page["items"]!.AsArray().Select(t => (string?)t!["id"]));
        }

        Assert.Equal([mine.ToString(), free.ToString()], ids);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/panel/tasks/{theirs}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync($"/api/panel/tasks/{theirs}/done", null, Ct)).StatusCode);

        var all = (await (await SignIn("lawyer")).GetFromJsonAsync<JsonNode>("/api/panel/tasks?scope=all", Ct))!;
        Assert.Equal(3, (int)all["total"]!);
    }

    [Fact]
    public async Task Task_detail_shows_the_clients_words_and_never_a_file_reference()
    {
        var task = await NewTask(type: "urgent_request");
        var c = await SignIn("yasmin");
        var body = await c.GetStringAsync($"/api/panel/tasks/{task}", Ct);
        var detail = System.Text.Json.Nodes.JsonNode.Parse(body)!;
        var words = detail["client_words"]!.AsArray();
        Assert.Equal("عايزة أكلم حد من المكتب", (string?)words[0]!["text"]);
        Assert.Equal("صورة العقد", (string?)words[1]!["caption"]);
        Assert.Equal("stored", (string?)words[1]!["media_status"]);
        Assert.Equal("الجلسة بكرة", (string?)words[2]!["transcript"]);
        foreach (var leak in new[] { "office-sensitive", "office-media", "secret-key", "عقد سارة", "sha256", "bucket", "\"key\"" })
        {
            Assert.DoesNotContain(leak, body);
        }

        Assert.True((bool)detail["can"]!["start"]!);
        Assert.False((bool)detail["can"]!["assign"]!);
    }

    [Fact]
    public async Task Tasks_move_through_S05_and_a_staff_member_who_starts_an_unassigned_task_takes_it()
    {
        var task = await NewTask();
        var c = await SignIn("yasmin");
        var started = (await (await c.PostAsync($"/api/panel/tasks/{task}/start", null, Ct)).Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal("in_progress", (string?)started["state"]);
        Assert.Equal(_staffA.ToString(), (string?)started["staff"]!["id"]);

        var again = await c.PostAsync($"/api/panel/tasks/{task}/start", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("transition_not_allowed", (string?)(await again.Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]);

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/panel/tasks/{task}/done", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/panel/tasks/{task}/cancel", null, Ct)).StatusCode);
        Assert.Equal("task.start|task.done", await db.ScalarAsync<string>(
            $"SELECT string_agg(action, '|' ORDER BY id) FROM hazem.panel_audit WHERE target_id = '{task}'"));
    }

    [Fact]
    public async Task The_lawyer_assigns_a_task_and_the_staff_member_then_sees_it()
    {
        var task = await NewTask(staff: _staffB);
        var lawyer = await SignIn("lawyer");
        var staff = (await lawyer.GetFromJsonAsync<JsonArray>("/api/panel/staff", Ct))!;
        Assert.Equal(2, staff.Count);
        Assert.Equal(HttpStatusCode.OK, (await lawyer.PostAsJsonAsync($"/api/panel/tasks/{task}/assign", new { staff_id = _staffA }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await SignIn("yasmin")).GetAsync($"/api/panel/tasks/{task}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await lawyer.PostAsJsonAsync($"/api/panel/tasks/{task}/assign", new { staff_id = Guid.NewGuid() }, Ct)).StatusCode);
    }

    // ---------- privacy and the database ----------

    [Fact]
    public async Task Logs_and_the_audit_trail_hold_no_password_email_or_client_words()
    {
        var task = await NewTask();
        var c = await SignIn("yasmin");
        await c.GetStringAsync($"/api/panel/tasks/{task}", Ct);
        await c.PostAsync($"/api/panel/tasks/{task}/start", null, Ct);
        var bad = _factory.CreateClient();
        await Token(bad);
        await Login(bad, "yasmin", "a wrong password typed here");

        var audit = await db.ScalarAsync<string>("SELECT string_agg(action || ' ' || detail::text, ' ') FROM hazem.panel_audit");
        var logs = string.Join("\n", _factory.Logs.Lines);
        foreach (var secret in new[] { Password, "a wrong password typed here", "yasmin@example.test", "عايزة أكلم حد", "سارة" })
        {
            Assert.DoesNotContain(secret, logs);
            Assert.DoesNotContain(secret, audit!);
        }
    }

    [Fact]
    public async Task Readonly_cannot_read_password_hashes_and_office_app_cannot_read_accounts()
    {
        Assert.True(await db.ScalarAsync<bool>("""
            SELECT has_column_privilege('office_readonly', 'hazem.panel_users', 'username', 'SELECT')
               AND NOT has_column_privilege('office_readonly', 'hazem.panel_users', 'password_hash', 'SELECT')
               AND NOT has_column_privilege('office_readonly', 'hazem.panel_users', 'security_stamp', 'SELECT')
               AND NOT has_table_privilege('office_readonly', 'hazem.panel_users', 'INSERT')
               AND has_table_privilege('office_readonly', 'hazem.panel_audit', 'SELECT')
            """));

        await using var app = new NpgsqlConnection(db.AppConnectionString);
        await app.OpenAsync(Ct);
        foreach (var table in new[] { "panel_users", "panel_audit" })
        {
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM hazem.{table}", app);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync(Ct))).SqlState);
        }
    }
}
