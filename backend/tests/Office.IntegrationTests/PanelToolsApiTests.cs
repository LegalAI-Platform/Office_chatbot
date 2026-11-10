using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Application.Panel;
using Office.Infrastructure.Lists;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 07c (DECISIONS v1.11) through the real API and database: alerts, a client's files, the
/// office's lists, unmuting, Hazem's approved texts with versions, and the month's report.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class PanelToolsApiTests(TestDatabase db) : IAsyncLifetime
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
            TRUNCATE hazem.panel_alerts, hazem.panel_alert_reads, hazem.ops_runs; DELETE FROM app.workflow_errors; DELETE FROM app.wa_turns WHERE wa_id LIKE '2010000009%';
            TRUNCATE hazem.turns, hazem.ai_usage, hazem.generated_replies, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
            DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
            UPDATE hazem.settings SET value = to_char((now() - interval '1 hour') AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
             WHERE key = 'alerts.since';
            """);
        _factory = new ApiFactory(db);
        await NewUser("keeper", Permissions.Maintenance); // v1.100: Hazem's texts are the maintenance account's only
        await NewUser("boss", Permissions.Manager);
        await NewUser("lawyer", Permissions.Lawyer);
        _staffA = (await NewUser("yasmin", Permissions.Staff, "ياسمين")).StaffId!.Value;
        _staffB = (await NewUser("karim", Permissions.Staff, "كريم")).StaffId!.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        // Texts edited here go away; the approved versions are active again. Only the keys edited here: a text switched off
        // by a migration (welcome_ask_name since 0034) stays off.
        await db.ExecAsync("""
            CREATE TEMP TABLE edited AS SELECT DISTINCT key FROM hazem.approved_texts WHERE source LIKE 'panel %';
            DELETE FROM hazem.approved_texts WHERE source LIKE 'panel %';
            UPDATE hazem.approved_texts t SET active = true
             WHERE t.key IN (SELECT key FROM edited)
               AND NOT EXISTS (SELECT 1 FROM hazem.approved_texts a WHERE a.key = t.key AND a.active)
               AND t.version = (SELECT max(version) FROM hazem.approved_texts m WHERE m.key = t.key);
            DROP TABLE edited;
            TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
            """);
    }

    // ---------- helpers ----------

    private async Task<PanelUser> NewUser(string name, string role, string? staff = null)
    {
        using var scope = _factory.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<PanelAccounts>().CreateAsync(null,
            new PanelAccounts.NewAccount(name, name, $"{name}@example.test", role, staff), Password, Ct);
        return r.Value.User;
    }

    private async Task<HttpClient> SignIn(string user)
    {
        var c = _factory.CreateClient();
        await Token(c);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/panel/auth/login", new { username = user, password = Password }, Ct)).StatusCode);
        await Token(c);
        return c;
    }

    private static async Task Token(HttpClient c)
    {
        var body = await c.GetFromJsonAsync<JsonNode>("/api/panel/auth/csrf", Ct);
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (string)body!["token"]!);
    }

    /// <summary>A task with a turn of a text and a picture; the task id is the person id.</summary>
    private async Task<Guid> NewTask(string type = "human_requested", Guid? staff = null, string state = "open", string due = "1 hour",
        string mime = "image/jpeg")
    {
        var person = Guid.NewGuid();
        var turn = "wt:" + Guid.NewGuid();
        var wa = "2010" + Random.Shared.Next(10_000_000, 99_999_999);
        var staffSql = staff is null ? "NULL" : $"'{staff}'";
        await db.ExecAsync($$$"""
            INSERT INTO hazem.persons (id, wa_id, name_raw, full_name_provided, source, first_seen_at)
              VALUES ('{{{person}}}', '{{{wa}}}', 'سارة محمد علي حسن', true, 'ad', now());
            INSERT INTO hazem.turns (turn_id, wa_id, messages, status, responder) VALUES ('{{{turn}}}', '{{{wa}}}', '[
              {"wamid":"w1","type":"text","text":"البطاقة"},
              {"wamid":"w2","type":"image","media":{"status":"stored","bucket":"office-sensitive","key":"wa/2026/10/{{{person}}}.jpg","mime_type":"{{{mime}}}","filename":"بطاقة سارة.jpg"}}
            ]'::jsonb, 'processed', 'hazem');
            INSERT INTO hazem.staff_tasks (id, person_id, turn_id, type, state, staff_id, due_at)
            VALUES ('{{{person}}}', '{{{person}}}', '{{{turn}}}', '{{{type}}}', '{{{state}}}', {{{staffSql}}}, now() + interval '{{{due}}}');
            """);
        _factory.Files.Objects[$"office-sensitive/wa/2026/10/{person}.jpg"] = ([1, 2, 3, 4], mime);
        return person;
    }

    private async Task<(int Made, int Mailed)> RunAlerts()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TaskMaintenance>().MarkOverdueAsync(Ct);
        var r = await scope.ServiceProvider.GetRequiredService<PanelAlerts>().RunAsync("http://127.0.0.1:8080", Ct);
        return (r.Made, r.Mailed);
    }

    // ---------- alerts ----------

    [Fact]
    public async Task Alerts_reach_who_may_see_the_task_once_and_the_email_has_no_client_details()
    {
        var urgent = await NewTask("urgent_request", staff: _staffA);
        var unassigned = await NewTask("human_requested");
        var late = await NewTask("service_request", staff: _staffB, due: "-5 minutes");

        var (made, _) = await RunAlerts();
        Assert.Equal(3, made);
        Assert.Equal((0, 0), await RunAlerts()); // once
        Assert.Equal("overdue", await db.ScalarAsync<string>($"SELECT state FROM hazem.staff_tasks WHERE id = '{late}'"));

        // E-mails: the urgent one to yasmin, the boss and the lawyer, never karim; nothing about the client.
        var urgentMails = _factory.Mail.Sent.Where(m => m.Text.Contains(urgent.ToString(), StringComparison.Ordinal)).Select(m => m.To).ToList();
        Assert.Equal(["boss@example.test", "lawyer@example.test", "yasmin@example.test"], urgentMails.Order(StringComparer.Ordinal));
        Assert.All(_factory.Mail.Sent, m =>
        {
            Assert.DoesNotContain("سارة", m.Text);
            Assert.DoesNotContain("2010", m.Text);
            Assert.DoesNotContain("البطاقة", m.Text);
        });
        Assert.Equal(4, _factory.Mail.Sent.Count(m => m.Text.Contains(unassigned.ToString(), StringComparison.Ordinal))); // everyone

        // The bell: yasmin sees hers and the unassigned one, not karim's.
        var yasmin = await SignIn("yasmin");
        var bell = (await yasmin.GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!;
        Assert.Equal(2, (int)bell["unread"]!);
        Assert.DoesNotContain(bell["items"]!.AsArray(), a => (Guid)a!["task_id"]! == late);
        await yasmin.PostAsJsonAsync("/api/panel/alerts/read", new { ids = (long[]?)null }, Ct);
        Assert.Equal(0, (int)(await yasmin.GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["unread"]!);
        var boss = await SignIn("boss");
        Assert.Equal(3, (int)(await boss.GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["unread"]!);
    }

    // ---------- TASK 13: the system's own alerts ----------

    [Fact]
    public async Task A_failed_nightly_job_or_n8n_failures_alert_the_managers_only_once_with_no_client_data()
    {
        await db.ExecAsync("""
            INSERT INTO hazem.ops_runs (kind, started_at, finished_at, ok, error_code) VALUES
              ('backup', now() - interval '5 minutes', now() - interval '4 minutes', false, 'failed_at_dump_office'),
              ('drill', now() - interval '5 minutes', now() - interval '4 minutes', true, NULL);
            INSERT INTO app.workflow_errors (workflow_name, node_name, message) VALUES
              ('META-WA-INBOUND', 'Send', 'سارة 2010****123 failed'), ('META-WA-INBOUND', 'Send', 'again');
            """);

        var (made, mailed) = await RunAlerts();
        Assert.Equal((2, 2), (made, mailed)); // the failed backup and the hour n8n failed in (the drill worked), to the one manager
        Assert.Equal((0, 0), await RunAlerts()); // once

        var mails = _factory.Mail.Sent.ToList();
        Assert.Equal(["boss@example.test", "boss@example.test"], mails.Select(m => m.To));
        Assert.Contains(mails, m => m.Subject.StartsWith("النسخة الاحتياطية فشلت |"));
        Assert.Contains(mails, m => m.Subject.StartsWith("فيه حاجات في n8n فشلت |"));
        Assert.All(mails, m =>
        {
            Assert.DoesNotContain("سارة", m.Text); // n8n's message never leaves its schema
            Assert.DoesNotContain("2010", m.Text);
            Assert.DoesNotContain("failed_at_dump_office", m.Text);
        });

        var boss = (await (await SignIn("boss")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!;
        Assert.Equal(["n8n", "backup"], boss["items"]!.AsArray().Select(a => (string)a!["system_what"]!).Order(StringComparer.Ordinal).Reverse());
        Assert.All(boss["items"]!.AsArray(), a => Assert.Null(a!["task_id"]));
        Assert.Equal(0, (int)(await (await SignIn("lawyer")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["unread"]!);
        Assert.Equal(0, (int)(await (await SignIn("yasmin")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["unread"]!);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_alerts WHERE kind = 'system' AND emailed_at IS NULL"));
    }

    [Fact]
    public async Task A_night_with_no_backup_alerts_the_managers_once_a_day()
    {
        // Never run yet (a new install): no alert. Then the last backup that worked is 30 hours old.
        Assert.Equal((0, 0), await RunAlerts());
        await db.ExecAsync("""
            INSERT INTO hazem.ops_runs (kind, started_at, finished_at, ok) VALUES ('backup', now() - interval '30 hours', now() - interval '30 hours', true);
            """);
        Assert.Equal((1, 1), await RunAlerts());
        Assert.Equal((0, 0), await RunAlerts()); // one a day
        Assert.StartsWith("النسخة الاحتياطية فشلت |", Assert.Single(_factory.Mail.Sent).Subject);
        Assert.StartsWith("missed:", await db.ScalarAsync<string>("SELECT system_ref FROM hazem.panel_alerts WHERE kind = 'system'"));
    }

    [Fact]
    public async Task A_client_turn_set_aside_after_too_many_tries_alerts_the_managers_once_with_no_client_data()
    {
        // Migration 0020: a turn the API failed on 5 times is failed with TOO_MANY_ATTEMPTS by wa_turns_claim.
        await db.ExecAsync("""
            INSERT INTO app.wa_turns (turn_id, wa_id, status, attempts, last_error) VALUES
              ('wt:00000000-0000-4000-8000-000000000901', '201000000901', 'failed', 5, '{"code":"TOO_MANY_ATTEMPTS"}'),
              ('wt:00000000-0000-4000-8000-000000000902', '201000000902', 'failed', 1, '{"code":"BAD_INPUT"}');
            """);

        Assert.Equal((1, 1), await RunAlerts()); // only the one set aside, not an ordinary failure
        Assert.Equal((0, 0), await RunAlerts()); // once
        var mail = Assert.Single(_factory.Mail.Sent);
        Assert.StartsWith("رسالة عميل وقفت بعد محاولات كتير |", mail.Subject);
        Assert.DoesNotContain("2010", mail.Text);
        Assert.Equal("turn:wt:00000000-0000-4000-8000-000000000901",
            await db.ScalarAsync<string>("SELECT system_ref FROM hazem.panel_alerts WHERE system_what = 'turn'"));
        Assert.Equal(0, (int)(await (await SignIn("yasmin")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["unread"]!);
    }

    [Fact]
    public async Task The_ai_down_for_three_messages_in_an_hour_alerts_the_managers_once_that_hour()
    {
        // 0032: two failures are no alert; a third in the same hour is one, with its e-mail (no client data). A low
        // confidence is the AI's own answer and never counts; a message the backstop read still does.
        await db.ExecAsync("""
            INSERT INTO hazem.classifications (turn_id, intent, confidence, full_name_present, fallback_reason, created_at) VALUES
              ('wt:ai-1', 'other', 0, false, 'error', now()),
              ('wt:ai-2', 'other', 0, false, 'low_confidence', now()),
              ('wt:ai-3', 'abusive', 0, false, 'backstop:error', now());
            """);
        Assert.Equal((0, 0), await RunAlerts());

        await db.ExecAsync("""
            INSERT INTO hazem.classifications (turn_id, intent, confidence, full_name_present, fallback_reason, created_at)
            SELECT 'wt:ai-4', 'other', 0, false, 'no_key', created_at FROM hazem.classifications WHERE turn_id = 'wt:ai-1';
            """);
        Assert.Equal((1, 1), await RunAlerts());
        Assert.Equal((0, 0), await RunAlerts()); // one an hour
        var mail = Assert.Single(_factory.Mail.Sent);
        Assert.StartsWith("الـ AI اللي بيفهم رسايل العملاء مش شغال |", mail.Subject);
        Assert.StartsWith("ai:", await db.ScalarAsync<string>("SELECT system_ref FROM hazem.panel_alerts WHERE system_what = 'ai'"));
    }

    [Fact]
    public async Task The_api_reads_the_nightly_jobs_but_never_writes_them_nor_n8n_s_errors()
    {
        await using var c = new Npgsql.NpgsqlConnection(db.ApiConnectionString);
        await c.OpenAsync(Ct);
        async Task<bool> Works(string sql)
        {
            try
            {
                await using var cmd = new Npgsql.NpgsqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync(Ct);
                return true;
            }
            catch (Npgsql.PostgresException)
            {
                return false;
            }
        }

        Assert.True(await Works("SELECT count(*) FROM hazem.ops_runs"));
        Assert.True(await Works("SELECT count(*) FROM hazem.n8n_error_hours(now() - interval '1 day')"));
        Assert.False(await Works("INSERT INTO hazem.ops_runs (kind) VALUES ('backup')"));
        Assert.False(await Works("SELECT message FROM app.workflow_errors"));
    }

    // ---------- files ----------

    [Fact]
    public async Task A_file_opens_for_whoever_may_see_the_task_and_every_opening_is_recorded()
    {
        var mine = await NewTask(staff: _staffA);
        var yasmin = await SignIn("yasmin");
        var r = await yasmin.GetAsync($"/api/panel/tasks/{mine}/files/1", Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/jpeg", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal([1, 2, 3, 4], await r.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("sandbox", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.panel_audit WHERE action = 'task.file_open' AND target_id = '{mine}'"));
        Assert.DoesNotContain("بطاقة", await db.ScalarAsync<string>("SELECT detail::text FROM hazem.panel_audit WHERE action = 'task.file_open'"));

        Assert.Equal(HttpStatusCode.NotFound, (await yasmin.GetAsync($"/api/panel/tasks/{mine}/files/0", Ct)).StatusCode); // a text, no file
        Assert.Equal(HttpStatusCode.NotFound, (await yasmin.GetAsync($"/api/panel/tasks/{mine}/files/9", Ct)).StatusCode);
        var karims = await NewTask(staff: _staffB);
        Assert.Equal(HttpStatusCode.NotFound, (await yasmin.GetAsync($"/api/panel/tasks/{karims}/files/1", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_file_that_could_run_in_the_panel_only_downloads()
    {
        var task = await NewTask(staff: _staffA, mime: "text/html");
        var r = await (await SignIn("boss")).GetAsync($"/api/panel/tasks/{task}/files/1", Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("attachment", r.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("client-file-2.jpg", r.Content.Headers.ContentDisposition.FileName?.Trim('"'));
    }

    [Fact]
    public async Task A_video_answers_a_byte_range_so_it_plays_on_an_iphone()
    {
        var task = await NewTask(staff: _staffA, mime: "video/mp4");
        var boss = await SignIn("boss");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/panel/tasks/{task}/files/1");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 2);
        var r = await boss.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.PartialContent, r.StatusCode);
        Assert.Equal([2, 3], await r.Content.ReadAsByteArrayAsync(Ct));
        Assert.Null(r.Content.Headers.ContentDisposition); // plays in the page
    }

    // ---------- lists and unmuting ----------

    [Fact]
    public async Task The_manager_and_the_lawyer_keep_the_lists_and_staff_cannot()
    {
        var lawyer = await SignIn("lawyer");
        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PostAsJsonAsync("/api/panel/lists",
            new { kind = "existing_client", phone = "0100 123 4567", name = "محمد علي", staff_name = "ياسمين" }, Ct)).StatusCode);
        Assert.Equal("invalid_phone", (string)(await (await lawyer.PostAsJsonAsync("/api/panel/lists",
            new { kind = "blocked", phone = "12" }, Ct)).Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]!);
        // A name that is not one of the staff makes no new staff member.
        var staffBefore = await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff");
        Assert.Equal("unknown_staff", (string)(await (await lawyer.PostAsJsonAsync("/api/panel/lists",
            new { kind = "existing_client", phone = "0100 123 4568", staff_name = "ياسمبن" }, Ct)).Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]!);
        Assert.Equal(staffBefore, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff"));
        var import = await lawyer.PostAsJsonAsync("/api/panel/lists/import",
            new { kind = "blocked", csv = "name,phone\nسبام,01011112222\n,bad", replace = false }, Ct);
        var imported = (await import.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal(1, (int)imported["imported"]!);
        Assert.Single(imported["rejected"]!.AsArray());

        var list = (await lawyer.GetFromJsonAsync<JsonNode>("/api/panel/lists?q=4567", Ct))!;
        var entry = Assert.Single(list["items"]!.AsArray())!;
        Assert.Equal("201001234567", (string)entry["wa_id"]!);
        Assert.Equal("ياسمين", (string)entry["staff_name"]!);
        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.DeleteAsync($"/api/panel/lists/{entry["id"]}", Ct)).StatusCode);

        var staff = await SignIn("karim");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/panel/lists", Ct)).StatusCode);
        Assert.Equal(3L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_audit WHERE action LIKE 'list.%'"));
    }

    [Fact]
    public async Task Only_the_manager_unmutes_a_conversation()
    {
        var person = await NewTask();
        var wa = await db.ScalarAsync<string>($"SELECT wa_id FROM hazem.persons WHERE id = '{person}'");
        await db.ExecAsync($"INSERT INTO hazem.conversations (id, person_id, state) VALUES (gen_random_uuid(), '{person}', 'muted')");

        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignIn("lawyer")).GetAsync("/api/panel/conversations/muted", Ct)).StatusCode);
        var boss = await SignIn("boss");
        var muted = (await boss.GetFromJsonAsync<JsonArray>("/api/panel/conversations/muted", Ct))!;
        Assert.Equal(wa, (string)Assert.Single(muted)!["wa_id"]!);
        Assert.Equal(HttpStatusCode.NoContent, (await boss.PostAsJsonAsync("/api/panel/conversations/unmute", new { wa_id = wa }, Ct)).StatusCode);
        Assert.Equal("bot_active", await db.ScalarAsync<string>($"SELECT state FROM hazem.conversations WHERE person_id = '{person}'"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_audit WHERE action = 'conversation.unmute'"));
    }

    // ---------- approved texts ----------

    [Fact]
    public async Task A_text_change_is_a_new_version_within_its_limit_and_an_old_one_comes_back()
    {
        var lawyer = await SignIn("keeper");
        await MaintenanceTwoFactor.TurnOnAsync(lawyer, Ct); // the maintenance account opens nothing without it
        var texts = (await lawyer.GetFromJsonAsync<JsonArray>("/api/panel/texts", Ct))!;
        var t = texts.First(x => (int)x!["max_chars"]! == 300)!;
        var key = (string)t["key"]!;
        var before = (int)t["version"]!;

        Assert.Equal("too_long", (string)(await (await lawyer.PostAsJsonAsync($"/api/panel/texts/{key}",
            new { text = new string('ا', 301) }, Ct)).Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]!);
        Assert.Equal("placeholder", (string)(await (await lawyer.PostAsJsonAsync($"/api/panel/texts/{key}",
            new { text = "اتصل بـ {{STAFF_PHONE}}" }, Ct)).Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]!);
        // Hazem treats [..] as an unfilled slot too and would stop sending the text.
        Assert.Equal("placeholder", (string)(await (await lawyer.PostAsJsonAsync($"/api/panel/texts/{key}",
            new { text = "اكتب [الاسم] هنا" }, Ct)).Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]!);
        var author = await db.ScalarAsync<string>($"SELECT coalesce(updated_by, '-') FROM hazem.approved_texts WHERE key = '{key}' AND version = {before}");

        var saved = await lawyer.PostAsJsonAsync($"/api/panel/texts/{key}", new { text = "نص جديد للتجربة" }, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("نص جديد للتجربة", await db.ScalarAsync<string>($"SELECT text FROM hazem.approved_texts WHERE key = '{key}' AND active"));

        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PostAsync($"/api/panel/texts/{key}/versions/{before}/activate", null, Ct)).StatusCode);
        Assert.Equal(before, await db.ScalarAsync<int>($"SELECT version FROM hazem.approved_texts WHERE key = '{key}' AND active"));
        var history = (await lawyer.GetFromJsonAsync<JsonArray>($"/api/panel/texts/{key}", Ct))!;
        Assert.True(history.Count >= 2);
        // Switching versions does not rewrite who wrote the old one.
        Assert.Equal(author, await db.ScalarAsync<string>($"SELECT coalesce(updated_by, '-') FROM hazem.approved_texts WHERE key = '{key}' AND version = {before}"));

        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignIn("yasmin")).GetAsync("/api/panel/texts", Ct)).StatusCode);
        // v1.100 (the owner: «انا مش عايز المكتب يعرف يغير اي نصوص»): no office role reads or changes them, the manager too
        foreach (var office in new[] { "boss", "lawyer" })
        {
            var c = await SignIn(office);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/panel/texts", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync($"/api/panel/texts/{key}", new { text = "تعديل من المكتب" }, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/panel/texts/{key}/versions/{before}/activate", null, Ct)).StatusCode);
        }

        Assert.Equal(before, await db.ScalarAsync<int>($"SELECT version FROM hazem.approved_texts WHERE key = '{key}' AND active"));
    }

    // ---------- reports ----------

    /// <summary>v1.58: the AI cost by conversation and by message (hazem.ai_usage), for the manager and the lawyer only.</summary>
    [Fact]
    public async Task V158_the_ai_cost_by_conversation_and_by_message_for_the_lawyer_only()
    {
        var person = await NewTask();
        var turn = await db.ScalarAsync<string>(
            $"SELECT t.turn_id FROM hazem.turns t JOIN hazem.persons p ON p.wa_id = t.wa_id WHERE p.id = '{person}'");
        await db.ExecAsync($"""
            INSERT INTO hazem.ai_usage (turn_id, kind, model, input_tokens, cache_read_tokens, cache_write_tokens, output_tokens, cost_usd) VALUES
              ('{turn}', 'classify', 'claude-opus-5-5', 500, 14000, 0, 190, 0.008800),
              ('{turn}', 'write', 'claude-haiku-5-5', 4400, 0, 0, 110, 0.000495),
              ('{turn}', 'review', 'some-other-model', 1500, 0, 0, 20, NULL);
            """);
        var lawyer = await SignIn("lawyer");

        var all = (await lawyer.GetFromJsonAsync<JsonNode>("/api/panel/reports/ai-cost", Ct))!;
        Assert.Equal(3, (int)all["summary"]!["calls"]!);
        Assert.Equal(1, (int)all["summary"]!["conversations"]!);
        Assert.Equal(1, (int)all["summary"]!["unpriced"]!);
        Assert.Equal(0.009295m, (decimal)all["summary"]!["total_usd"]!);
        Assert.Equal(20400, (long)all["summary"]!["input_tokens"]!); // 500 + 14000 (cache read) + 4400 + 1500, every kind of input
        var row = Assert.Single(all["conversations"]!.AsArray())!;
        Assert.Equal("سارة محمد علي حسن", (string)row["client"]!);
        Assert.Equal(1, (int)row["turns"]!);
        Assert.Equal(person, (Guid)row["person_id"]!);

        var turns = (await lawyer.GetFromJsonAsync<JsonNode>($"/api/panel/reports/ai-cost/{person}", Ct))!["turns"]!.AsArray();
        var calls = Assert.Single(turns)!["calls"]!.AsArray();
        Assert.Equal(["classify", "write", "review"], calls.Select(c => (string)c!["kind"]!).ToArray());
        Assert.Equal(14000, (int)calls[0]!["cache_read_tokens"]!);
        Assert.Null(calls[2]!["cost_usd"]);

        // the month's report carries the number the dashboard shows
        var month = (await lawyer.GetFromJsonAsync<JsonNode>("/api/panel/reports", Ct))!;
        Assert.Equal(0.009295m, (decimal)month["ai_cost"]!["total_usd"]!);

        Assert.Equal(HttpStatusCode.BadRequest, (await lawyer.GetAsync("/api/panel/reports/ai-cost?month=2026-13", Ct)).StatusCode);
        var karim = await SignIn("karim");
        Assert.Equal(HttpStatusCode.Forbidden, (await karim.GetAsync("/api/panel/reports/ai-cost", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await karim.GetAsync($"/api/panel/reports/ai-cost/{person}", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_month_report_counts_tasks_by_staff_and_new_clients_by_source()
    {
        await NewTask("urgent_request", staff: _staffA);
        var done = await NewTask("service_request", staff: _staffA);
        await db.ExecAsync($"UPDATE hazem.staff_tasks SET state = 'done' WHERE id = '{done}'");
        var lawyer = await SignIn("lawyer");
        var r = (await lawyer.GetFromJsonAsync<JsonNode>("/api/panel/reports", Ct))!;
        Assert.Equal(2, (int)r["tasks"]!);
        Assert.Equal(1, (int)r["done"]!);
        Assert.Equal(1, (int)r["done_on_time"]!);
        Assert.Equal(2, (int)r["new_clients"]!);
        Assert.Equal("ad", (string)r["by_source"]![0]!["key"]!);
        Assert.Equal(2, (int)r["by_staff"]!.AsArray().Single(s => (string)s!["staff"]! == "ياسمين")!["assigned"]!);
        Assert.Equal(HttpStatusCode.BadRequest, (await lawyer.GetAsync("/api/panel/reports?month=2026-13", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignIn("karim")).GetAsync("/api/panel/reports", Ct)).StatusCode);

        // the owner (2026-11-07): a day, a range or a year too, and the money of the last 12 months
        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var day = (await lawyer.GetFromJsonAsync<JsonNode>($"/api/panel/reports?from={today}&to={today}", Ct))!;
        Assert.Equal((today, 2), ((string)day["month"]!, (int)day["new_clients"]!)); // both clients came today
        Assert.Equal(HttpStatusCode.BadRequest, (await lawyer.GetAsync("/api/panel/reports?from=2026-11-07&to=2026-11-01", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lawyer.GetAsync("/api/panel/reports?from=2025-01-01&to=2026-11-07", Ct)).StatusCode); // over a year
        var trend = (await lawyer.GetFromJsonAsync<JsonNode>("/api/panel/reports/trend", Ct))!["months"]!.AsArray();
        Assert.Equal(12, trend.Count);
        Assert.Equal(today[..7], (string)trend[^1]!["month"]!); // up to this month
    }
}
