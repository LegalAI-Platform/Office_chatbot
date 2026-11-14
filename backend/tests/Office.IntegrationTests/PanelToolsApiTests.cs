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

    /// <summary>v1.106: the worker's look at the office's data (off in tests, like the alerts): the areas it woke the streams for.</summary>
    private async Task<IReadOnlyList<string>> WatchData()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PanelAlerts>().WatchDataAsync(Ct);
    }

    // ---------- alerts ----------

    // fix/alert-flood (the owner, v1.108: «التنبيه يروح للي عليه الدور بس» and «الإيميل للعاجل والمتأخر بس، وللمسؤول بس»):
    // an alert is the job of the task's holder, else of its audience (the staff, the lawyer, the managers); only the urgent
    // and the overdue ones are e-mailed.
    [Fact]
    public async Task Alerts_reach_whose_job_they_are_once_and_only_the_urgent_and_the_late_are_emailed_without_client_details()
    {
        var urgent = await NewTask("urgent_request", staff: _staffA);
        var unassigned = await NewTask("human_requested");
        var late = await NewTask("service_request", staff: _staffB, due: "-5 minutes");

        var (made, _) = await RunAlerts();
        Assert.Equal(4, made); // v1.105: the late one is new too (task_new, never mailed)
        Assert.Equal((0, 0), await RunAlerts()); // once
        Assert.Equal("overdue", await db.ScalarAsync<string>($"SELECT state FROM hazem.staff_tasks WHERE id = '{late}'"));

        // E-mails: the urgent one to yasmin, who holds it; the late one to karim, who holds it; nothing for the client
        // asking for someone (the bell only); and nothing about the client in any of them.
        string[] To(Guid task) => [.. _factory.Mail.Sent.Where(m => m.Text.Contains(task.ToString(), StringComparison.Ordinal)).Select(m => m.To).Order(StringComparer.Ordinal)];
        Assert.Equal(["yasmin@example.test"], To(urgent));
        Assert.Equal(["karim@example.test"], To(late));
        Assert.Empty(To(unassigned));
        Assert.All(_factory.Mail.Sent, m =>
        {
            Assert.DoesNotContain("سارة", m.Text);
            Assert.DoesNotContain("2010", m.Text);
            Assert.DoesNotContain("البطاقة", m.Text);
        });

        // The bell: yasmin hears hers and the unassigned one (the staff's), not karim's.
        var yasmin = await SignIn("yasmin");
        var bell = (await yasmin.GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!;
        Assert.Equal(2, (int)bell["unread"]!);
        Assert.DoesNotContain(bell["items"]!.AsArray(), a => (Guid)a!["task_id"]! == late);
        await yasmin.PostAsJsonAsync("/api/panel/alerts/read", new { ids = (long[]?)null }, Ct);
        Assert.Equal(0, (int)(await yasmin.GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["unread"]!);
        // karim: his late task's two, and the unassigned one
        Assert.Equal(3, (int)(await (await SignIn("karim")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["unread"]!);
        // the boss and the lawyer: none of these is their job (the boss: the system's and a low rating; the lawyer: an
        // urgent alert nobody holds, pricing, an Urfi review)
        Assert.Equal(0, (int)(await (await SignIn("boss")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["unread"]!);
        Assert.Equal(0, (int)(await (await SignIn("lawyer")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["unread"]!);
    }

    // fix/alert-flood: an urgent alert nobody holds is the lawyer's and the staff's both (nothing escalates it later); a
    // service to price is the lawyer's only; with no active staff account, the staff's alerts are the managers'.
    [Fact]
    public async Task An_unheld_urgent_alert_reaches_the_lawyer_and_the_staff_and_with_no_staff_the_managers_hear_the_staff_s()
    {
        var urgent = await NewTask("urgent_request");
        var pricing = await NewTask("service_pricing");
        var asking = await NewTask("existing_client_message");
        await RunAlerts();
        string[] To(Guid task) => [.. _factory.Mail.Sent.Where(m => m.Text.Contains(task.ToString(), StringComparison.Ordinal)).Select(m => m.To).Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(["karim@example.test", "lawyer@example.test", "yasmin@example.test"], To(urgent));
        Assert.Empty(To(pricing)); // a new task: the bell only
        async Task<Guid[]> Bell(string who) =>
            [.. (await (await SignIn(who)).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["items"]!.AsArray().Select(a => (Guid)a!["task_id"]!).Distinct().Order()];
        Assert.Equal(new[] { urgent, pricing }.Order(), await Bell("lawyer"));
        Assert.Equal(new[] { urgent, asking }.Order(), await Bell("yasmin"));
        Assert.Empty(await Bell("boss"));

        // no active staff account any more: the boss hears the staff's (the client asking), not the lawyer's pricing
        await db.ExecAsync("UPDATE hazem.panel_users SET active = false WHERE role = 'staff'");
        Assert.Equal(new[] { urgent, asking }.Order(), await Bell("boss"));
    }

    // v1.110 (the owner: «كميه ايميلات كتيره اوي» … «موافق» … «سيبهم»): a paid urgent call is one task. While the lawyer's
    // WhatsApp alert is his, its urgent alert is the bell's and Windows' only; when that alert did not reach him, the lawyer's
    // e-mail; when nobody took it in time, the staff's; never a «late» e-mail for it. The bell shows its urgent line to the
    // lawyer and the staff, as any urgent one.
    [Fact]
    public async Task A_paid_urgent_call_emails_the_lawyer_only_when_whatsapp_failed_and_the_staff_only_when_nobody_took_it()
    {
        var waiting = await NewTask("urgent_call");
        var failed = await NewTask("urgent_call_failed");
        var unanswered = await NewTask("urgent_call_unanswered");
        var late = await NewTask("urgent_call", due: "-5 minutes");
        await RunAlerts();
        string[] To(Guid task) => [.. _factory.Mail.Sent.Where(m => m.Text.Contains(task.ToString(), StringComparison.Ordinal)).Select(m => m.To).Distinct().Order(StringComparer.Ordinal)];
        Assert.Empty(To(waiting));
        Assert.Equal(["lawyer@example.test"], To(failed));
        Assert.Equal(["karim@example.test", "yasmin@example.test"], To(unanswered));
        Assert.Empty(To(late)); // neither its urgent alert nor its late one
        Assert.Equal("task_overdue,task_urgent", await db.ScalarAsync<string>(
            $"SELECT string_agg(kind, ',' ORDER BY kind) FROM hazem.panel_alerts WHERE task_id = '{late}'")); // the bell has both

        async Task<Guid[]> Bell(string who) =>
            [.. (await (await SignIn(who)).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["items"]!.AsArray().Select(a => (Guid)a!["task_id"]!).Distinct().Order()];
        Guid[] all = [.. new[] { waiting, failed, unanswered, late }.Order()];
        Assert.Equal(all, await Bell("lawyer"));
        Assert.Equal(all, await Bell("yasmin"));
    }

    /// <summary>v1.110: a client of this name and number, for the search's tests.</summary>
    private async Task<Guid> Client(string name, string wa)
    {
        var person = Guid.NewGuid();
        await db.ExecAsync($"""
            INSERT INTO hazem.persons (id, wa_id, name_raw, full_name_provided, source, first_seen_at)
            VALUES ('{person}', '{wa}', '{name}', true, 'ad', now());
            """);
        return person;
    }

    private async Task<Guid> TaskOf(Guid person, string type, Guid? staff = null, string state = "open")
    {
        var id = Guid.NewGuid();
        await db.ExecAsync($"""
            INSERT INTO hazem.staff_tasks (id, person_id, turn_id, type, state, staff_id, due_at)
            VALUES ('{id}', '{person}', 'wt:{id}', '{type}', '{state}', {(staff is null ? "NULL" : $"'{staff}'")}, now() + interval '1 hour');
            """);
        return id;
    }

    // v1.110 (the owner: «السيرش بار هنا حاسه بيسرش غلط» … «اعمل بحث شامل»): the search bar's one search finds the clients,
    // their appointments and the tasks: a part of the name in any order, through one form of the letters, or the end of the
    // number, in Arabic digits or with spaces too. A staff member finds only the tasks she may see, and the number never
    // comes back whole. The tasks page's own search finds the same clients.
    [Fact]
    public async Task The_search_bar_finds_clients_appointments_and_tasks_by_any_form_of_the_name_or_the_number_s_end()
    {
        var ahmed = await Client("أحمد مصطفى العمدة", "201000000713");
        var fatma = await Client("فاطمة علي", "201000000824");
        var other = await Client("كريم سامي", "201000000935");
        var open = await TaskOf(ahmed, "complaint");
        var done = await TaskOf(ahmed, "human_requested", state: "done");
        var karims = await TaskOf(fatma, "service_request", staff: _staffB);
        await TaskOf(other, "complaint");
        var visit = Guid.NewGuid();
        await db.ExecAsync($"""
            INSERT INTO hazem.appointments (id, person_id, service, starts_at, ends_at, state, amount, booked_turn_id)
            VALUES ('{visit}', '{ahmed}', 'consultation_office', now() + interval '1 day', now() + interval '1 day 30 minutes',
                    'confirmed', 500, 'wt:{visit}');
            """);
        try
        {
            var boss = await SignIn("boss");
            async Task<JsonNode> Search(HttpClient c, string q)
            {
                var r = await c.PostAsJsonAsync("/api/panel/search", new { q }, Ct);
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
            }

            static Guid[] Ids(JsonNode r, string kind) => [.. r[kind]!.AsArray().Select(x => (Guid)x!["id"]!)];

            // one form of the letters, the words in any order
            foreach (var q in new[] { "احمد", "مصطفي العمده", "العمدة أحمد" })
            {
                var r = await Search(boss, q);
                Assert.Equal([ahmed], Ids(r, "clients"));
                Assert.Equal([visit], Ids(r, "appointments"));
                Assert.Equal([open, done], Ids(r, "tasks")); // the unfinished first, a finished one too
            }

            Assert.Equal([fatma], Ids(await Search(boss, "فاطمه"), "clients"));
            // the number's end: Arabic digits, spaces, the whole number as WhatsApp or as written in Egypt
            foreach (var q in new[] { "713", "٧١٣", "0100 000 0713", "+20 100 000 0713" })
            {
                Assert.Equal([ahmed], Ids(await Search(boss, q), "clients"));
            }

            Assert.Equal([ahmed], Ids(await Search(boss, "احمد 713"), "clients")); // a name and a number together
            Assert.Empty(Ids(await Search(boss, "احمد 824"), "clients"));

            // the client: his number's last 3 digits only, and where the panel opens him (his unfinished task)
            var found = await Search(boss, "713");
            var client = found["clients"]![0]!;
            Assert.Equal(("713", open, 1), ((string)client["phone_last3"]!, (Guid)client["task_id"]!, (int)client["open_tasks"]!));
            Assert.DoesNotContain("201000000713", found.ToJsonString(), StringComparison.Ordinal);

            // yasmin: her tasks and the unassigned, never karim's; a client with nothing for her to open is not listed
            var yasmin = await SignIn("yasmin");
            var forYasmin = await Search(yasmin, "فاطمه");
            Assert.Empty(Ids(forYasmin, "tasks"));
            Assert.Empty(Ids(forYasmin, "clients"));
            Assert.Equal([karims], Ids(await Search(boss, "فاطمه"), "tasks"));

            // too short, or no one
            Assert.Empty(Ids(await Search(boss, "71"), "clients"));
            Assert.Empty(Ids(await Search(boss, "لا يوجد اسم كهذا"), "clients"));

            // the tasks page's search finds the same clients
            var page = (await boss.GetFromJsonAsync<JsonNode>($"/api/panel/tasks?scope=all&state=active&q={Uri.EscapeDataString("مصطفي")}", Ct))!;
            Assert.Equal([open], page["items"]!.AsArray().Select(x => (Guid)x!["id"]!));
        }
        finally
        {
            await db.ExecAsync($"DELETE FROM hazem.appointments WHERE id = '{visit}'"); // before the next test empties the persons
        }
    }

    // v1.110 (the owner: «…/documents شوف دي عاوز تعملها» … «اعمل كله مع بعض»): «المستندات» lists every file a client sent,
    // by client, for whoever may see his tasks (a staff member: hers and the unassigned), and opens one through the panel
    // only, recorded; never another staff member's client's.
    [Fact]
    public async Task The_documents_page_lists_each_client_s_files_for_whoever_may_see_him_and_opens_them_recorded()
    {
        var mine = await NewTask("complaint", staff: _staffA);                              // yasmin's client: a picture
        var others = await NewTask("complaint", staff: _staffB, mime: "application/pdf");   // karim's client: a PDF
        var boss = await SignIn("boss");
        var yasmin = await SignIn("yasmin");
        async Task<JsonNode> List(HttpClient c, object body)
        {
            var r = await c.PostAsJsonAsync("/api/panel/documents", body, Ct);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        }

        static Guid[] Clients(JsonNode p) => [.. p["clients"]!.AsArray().Select(c => (Guid)c!["id"]!).Order()];
        static JsonNode FirstFile(JsonNode p, Guid client) => p["clients"]!.AsArray().Single(c => (Guid)c!["id"]! == client)!["files"]![0]!;

        var all = await List(boss, new { });
        Assert.Equal(new[] { mine, others }.Order(), Clients(all));
        Assert.Equal(2, (int)all["total"]!);
        var picture = FirstFile(all, mine);
        // the picture of the turn's second message, with the task opened from it (NewTask: the task id is the person's)
        Assert.Equal(("message", "image", 1, mine), ((string)picture["source"]!, (string)picture["kind"]!, (int)picture["index"]!, (Guid)picture["task_id"]!));
        Assert.Equal(1, (int)all["clients"]!.AsArray().Single(c => (Guid)c!["id"]! == mine)!["count"]!);

        Assert.Equal([mine], Clients(await List(yasmin, new { })));                // not karim's client
        Assert.Equal([others], Clients(await List(boss, new { kind = "document" })));
        Assert.Empty(Clients(await List(boss, new { source = "receipt" })));
        Assert.Equal(new[] { mine, others }.Order(), Clients(await List(boss, new { q = "ساره" }))); // «سارة محمد علي حسن»
        Assert.Empty(Clients(await List(boss, new { q = "71" })));                   // names no one: nothing, never everyone

        // opening: through the panel, recorded
        var opened = await yasmin.GetAsync($"/api/panel/documents/message/{(Guid)picture["id"]!}/1", Ct);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal("image/jpeg", opened.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_audit WHERE action = 'document.open'"));
        var pdf = FirstFile(all, others);
        Assert.Equal(HttpStatusCode.NotFound, (await yasmin.GetAsync($"/api/panel/documents/message/{(Guid)pdf["id"]!}/1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await boss.GetAsync($"/api/panel/documents/message/{(Guid)pdf["id"]!}/1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await boss.GetAsync($"/api/panel/documents/message/{(Guid)pdf["id"]!}/0", Ct)).StatusCode); // a text
        Assert.Equal(HttpStatusCode.NotFound, (await boss.GetAsync($"/api/panel/documents/elsewhere/{(Guid)pdf["id"]!}/1", Ct)).StatusCode);
    }

    // v1.110 (the owner, of the bell's red count: «أيوه، ضيفها»): a finished task (done, cancelled) leaves its alerts read
    // for everyone: out of the red count, and still in the list, dimmed.
    [Fact]
    public async Task A_finished_task_s_alerts_are_read_for_everyone_and_stay_in_the_list()
    {
        var urgent = await NewTask("urgent_request");
        var complaint = await NewTask("complaint");
        await RunAlerts();
        var yasmin = await SignIn("yasmin");
        async Task<JsonNode> Bell() => (await yasmin.GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!;
        Assert.Equal(2, (int)(await Bell())["unread"]!);

        await db.ExecAsync($"UPDATE hazem.staff_tasks SET state = 'done' WHERE id = '{urgent}'");
        Assert.Equal(1, (int)(await Bell())["unread"]!);
        await db.ExecAsync($"UPDATE hazem.staff_tasks SET state = 'cancelled' WHERE id = '{complaint}'");
        var bell = await Bell();
        Assert.Equal(0, (int)bell["unread"]!);
        Assert.Equal(2, bell["items"]!.AsArray().Count);
        Assert.All(bell["items"]!.AsArray(), a => Assert.True((bool)a!["read"]!));
    }

    // v1.105 (the owner: «العميل … اجابته لازم موظف يتدخل ليه فيها … الحاجات دي انا عايزها تيجي في النوتيفيكيشن … نشوف
    // حل عام»): every task Hazem opens for the office alerts at once, whatever its type; never by e-mail.
    [Fact]
    public async Task Every_new_task_Hazem_opens_alerts_at_once_whatever_its_type_with_no_email_and_no_backlog()
    {
        var asking = await NewTask("existing_client_message", staff: _staffA);
        var complaint = await NewTask("complaint");
        var urgent = await NewTask("urgent_request", staff: _staffA);
        var before = await NewTask("service_request");
        await db.ExecAsync($"UPDATE hazem.staff_tasks SET created_at = now() - interval '20 minutes' WHERE id = '{before}'");

        await RunAlerts();
        async Task<string> Kinds(Guid task) =>
            (await db.ScalarAsync<string>($"SELECT coalesce(string_agg(kind, ',' ORDER BY kind), '') FROM hazem.panel_alerts WHERE task_id = '{task}'"))!;
        Assert.Equal("task_new", await Kinds(asking));
        Assert.Equal("task_new", await Kinds(complaint));
        Assert.Equal("task_urgent", await Kinds(urgent)); // its own alert already says it
        Assert.Equal("", await Kinds(before)); // older than 10 minutes: a deploy brings no backlog

        // the bell and Windows only: no e-mail for a client's message
        Assert.DoesNotContain(_factory.Mail.Sent, m => m.Text.Contains(asking.ToString(), StringComparison.Ordinal) || m.Text.Contains(complaint.ToString(), StringComparison.Ordinal));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_alerts WHERE kind = 'task_new' AND emailed_at IS NOT NULL"));

        // yasmin hears hers and the unassigned one
        var bell = (await (await SignIn("yasmin")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["items"]!.AsArray();
        Assert.Equal(
            [(asking, "task_new"), (complaint, "task_new"), (urgent, "task_urgent")],
            bell.Select(a => ((Guid)a!["task_id"]!, (string)a["kind"]!)).OrderBy(x => x.Item2, StringComparer.Ordinal).ThenBy(x => x.Item1 == asking ? 0 : 1).ToList());
    }

    // v1.104: the Windows notification says who and what without opening the panel (the owner: «ما فيهاش تفاصيل»), but
    // never the number and never the client's words («من غير كلامه»).
    [Fact]
    public async Task The_bell_gives_the_number_s_last_3_digits_the_matter_and_who_has_the_task_never_the_client_s_words()
    {
        var mine = await NewTask("urgent_request", staff: _staffA);
        var nobody = await NewTask("human_requested");
        var intake = Guid.NewGuid();
        await db.ExecAsync($"""
            INSERT INTO hazem.intakes (id, person_id, state, matter_type) VALUES ('{intake}', '{mine}', 'handed_to_staff', 'personal_status');
            UPDATE hazem.staff_tasks SET intake_id = '{intake}' WHERE id = '{mine}';
            """);
        await RunAlerts();

        var bell = (await (await SignIn("yasmin")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!["items"]!.AsArray();
        var wa = (await db.ScalarAsync<string>($"SELECT wa_id FROM hazem.persons WHERE id = '{mine}'"))!;
        var a = bell.Single(x => (Guid?)x!["task_id"] == mine)!;
        Assert.Equal(wa[^3..], (string)a["phone_last3"]!);
        Assert.Equal("personal_status", (string)a["matter_type"]!);
        Assert.Equal("ياسمين", (string)a["staff_name"]!);
        Assert.Equal(
            ["client_name", "created_at", "due_at", "id", "kind", "matter_type", "phone_last3", "read", "staff_name", "task_id", "task_type"],
            a.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));

        var b = bell.Single(x => (Guid?)x!["task_id"] == nobody)!;
        Assert.Null(b["staff_name"]); // nobody has it yet
        Assert.Null(b["matter_type"]);
        Assert.DoesNotContain(wa, bell.ToJsonString()); // the last 3 digits only
    }

    // v1.104 (the owner: «ريل تايم»): the open bell hears a new alert at once, with no poll; only a session may listen.
    [Fact]
    public async Task The_bell_s_stream_says_at_once_that_alerts_were_made_and_needs_a_session()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/panel/alerts/stream", Ct)).StatusCode);

        var yasmin = await SignIn("yasmin");
        using var r = await yasmin.GetAsync("/api/panel/alerts/stream", HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/event-stream", r.Content.Headers.ContentType?.MediaType);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        wait.CancelAfter(TimeSpan.FromSeconds(20));
        using var body = new StreamReader(await r.Content.ReadAsStreamAsync(wait.Token));
        Assert.Equal("retry: 5000", await body.ReadLineAsync(wait.Token));
        Assert.Equal("", await body.ReadLineAsync(wait.Token));

        await NewTask("urgent_request", staff: _staffA);
        await RunAlerts();
        Assert.Equal("event: alerts", await body.ReadLineAsync(wait.Token)); // no client data in it: the page asks /alerts
        Assert.Equal("data: new", await body.ReadLineAsync(wait.Token));
    }

    // v1.106 (the owner: «الداش بورد كلها ريل تايم ؟» … «عايزين كله يشتغل بنفس الطريقه دي»): the same stream names the areas
    // of the office's data that changed, whoever changed them (here the database itself, as Hazem and n8n do), so every
    // page on the screen that shows them asks again at once. Nothing changed, nothing said; and it carries no row.
    [Fact]
    public async Task The_stream_names_the_areas_of_the_office_s_data_that_changed_and_says_nothing_when_nothing_did()
    {
        var yasmin = await SignIn("yasmin");
        using var r = await yasmin.GetAsync("/api/panel/alerts/stream", HttpCompletionOption.ResponseHeadersRead, Ct);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        wait.CancelAfter(TimeSpan.FromSeconds(20));
        using var body = new StreamReader(await r.Content.ReadAsStreamAsync(wait.Token));
        Assert.Equal("retry: 5000", await body.ReadLineAsync(wait.Token));
        Assert.Equal("", await body.ReadLineAsync(wait.Token));
        async Task Said(string areas)
        {
            Assert.Equal("event: data", await body.ReadLineAsync(wait.Token));
            Assert.Equal("data: " + areas, await body.ReadLineAsync(wait.Token));
            Assert.Equal("", await body.ReadLineAsync(wait.Token));
        }

        Assert.Empty(await WatchData()); // the first look only notes where things are
        Assert.Empty(await WatchData()); // nothing changed

        var task = await NewTask("complaint"); // a new client with a new task
        Assert.Equal(["clients", "tasks"], await WatchData());
        await Said("clients,tasks");
        Assert.Empty(await WatchData()); // once

        await db.ExecAsync($"UPDATE hazem.staff_tasks SET staff_id = '{_staffA}' WHERE id = '{task}'"); // the same task, assigned
        Assert.Equal(["tasks"], await WatchData());
        await Said("tasks");

        var appointment = Guid.NewGuid();
        await db.ExecAsync($"""
            INSERT INTO hazem.appointments (id, person_id, service, starts_at, ends_at, amount, booked_turn_id)
            VALUES ('{appointment}', '{task}', 'consultation_office', now() + interval '300 days', now() + interval '300 days 30 minutes', 1000, 'wt:live')
            """);
        try
        {
            Assert.Equal(["appointments"], await WatchData()); // a booking
            await Said("appointments");
        }
        finally
        {
            await db.ExecAsync($"DELETE FROM hazem.appointments WHERE id = '{appointment}'");
        }

        Assert.Equal(["appointments"], await WatchData()); // and gone
        await Said("appointments");

        // any change counts, even one that writes the same values: here Hazem's texts (their settings page)
        await db.ExecAsync("UPDATE hazem.approved_texts SET active = active WHERE key = (SELECT min(key) FROM hazem.approved_texts)");
        Assert.Equal(["texts"], await WatchData());
        await Said("texts");
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
        Assert.Contains(mails, m => m.Subject.StartsWith("فشلت النسخة الاحتياطية |"));
        Assert.Contains(mails, m => m.Subject.StartsWith("توجد عمليات فاشلة في n8n |"));
        Assert.All(mails, m =>
        {
            Assert.DoesNotContain("سارة", m.Text); // n8n's message never leaves its schema
            Assert.DoesNotContain("2010", m.Text);
            Assert.DoesNotContain("failed_at_dump_office", m.Text);
        });

        var boss = (await (await SignIn("boss")).GetFromJsonAsync<JsonNode>("/api/panel/alerts", Ct))!;
        Assert.Equal(["n8n", "backup"], boss["items"]!.AsArray().Select(a => (string)a!["system_what"]!).Order(StringComparer.Ordinal).Reverse());
        Assert.All(boss["items"]!.AsArray(), a => Assert.Null(a!["task_id"]));
        Assert.Equal((2, 2), ((int)boss["unread"]!, (int)boss["unread_system"]!)); // v1.104: the bell counts the requests apart
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
        Assert.StartsWith("فشلت النسخة الاحتياطية |", Assert.Single(_factory.Mail.Sent).Subject);
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
        Assert.StartsWith("توقفت معالجة رسالة عميل بعد محاولات عديدة |", mail.Subject);
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
        Assert.StartsWith("الذكاء الاصطناعي الذي يفهم رسائل العملاء متوقف |", mail.Subject);
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
        await NewUser("keeper", Permissions.Maintenance); // v1.100: Hazem's texts are the maintenance account's only
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

    /// <summary>
    /// v1.106 (the owner: «التقارير ما يشوفهاش اكونت المستشار … اكونت صيانه بس»): the reports are the maintenance account's
    /// only, signed in with its authenticator on (it opens nothing without it). No office role reads them, the manager too.
    /// </summary>
    private async Task<HttpClient> Reader()
    {
        await NewUser("keeper", Permissions.Maintenance);
        var keeper = await SignIn("keeper");
        await MaintenanceTwoFactor.TurnOnAsync(keeper, Ct);
        foreach (var office in new[] { "boss", "lawyer", "karim" })
        {
            var c = await SignIn(office);
            foreach (var path in new[] { "/api/panel/reports", "/api/panel/reports/trend", "/api/panel/reports/ai-cost" })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(path, Ct)).StatusCode);
            }
        }

        return keeper;
    }

    /// <summary>v1.58: the AI cost by conversation and by message (hazem.ai_usage); v1.106: for the maintenance account only.</summary>
    [Fact]
    public async Task V158_the_ai_cost_by_conversation_and_by_message_for_the_maintenance_account_only()
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
        var keeper = await Reader(); // v1.106: the maintenance account

        var all = (await keeper.GetFromJsonAsync<JsonNode>("/api/panel/reports/ai-cost", Ct))!;
        Assert.Equal(3, (int)all["summary"]!["calls"]!);
        Assert.Equal(1, (int)all["summary"]!["conversations"]!);
        Assert.Equal(1, (int)all["summary"]!["unpriced"]!);
        Assert.Equal(0.009295m, (decimal)all["summary"]!["total_usd"]!);
        Assert.Equal(20400, (long)all["summary"]!["input_tokens"]!); // 500 + 14000 (cache read) + 4400 + 1500, every kind of input
        var row = Assert.Single(all["conversations"]!.AsArray())!;
        Assert.Equal("سارة محمد علي حسن", (string)row["client"]!);
        Assert.Equal(1, (int)row["turns"]!);
        Assert.Equal(person, (Guid)row["person_id"]!);

        var turns = (await keeper.GetFromJsonAsync<JsonNode>($"/api/panel/reports/ai-cost/{person}", Ct))!["turns"]!.AsArray();
        var calls = Assert.Single(turns)!["calls"]!.AsArray();
        Assert.Equal(["classify", "write", "review"], calls.Select(c => (string)c!["kind"]!).ToArray());
        Assert.Equal(14000, (int)calls[0]!["cache_read_tokens"]!);
        Assert.Null(calls[2]!["cost_usd"]);

        // the month's report carries the number the dashboard shows
        var month = (await keeper.GetFromJsonAsync<JsonNode>("/api/panel/reports", Ct))!;
        Assert.Equal(0.009295m, (decimal)month["ai_cost"]!["total_usd"]!);

        Assert.Equal(HttpStatusCode.BadRequest, (await keeper.GetAsync("/api/panel/reports/ai-cost?month=2026-13", Ct)).StatusCode);
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
        var keeper = await Reader(); // v1.106: the maintenance account
        var r = (await keeper.GetFromJsonAsync<JsonNode>("/api/panel/reports", Ct))!;
        Assert.Equal(2, (int)r["tasks"]!);
        Assert.Equal(1, (int)r["done"]!);
        Assert.Equal(1, (int)r["done_on_time"]!);
        Assert.Equal(2, (int)r["new_clients"]!);
        Assert.Equal("ad", (string)r["by_source"]![0]!["key"]!);
        Assert.Equal(2, (int)r["by_staff"]!.AsArray().Single(s => (string)s!["staff"]! == "ياسمين")!["assigned"]!);
        Assert.Equal(HttpStatusCode.BadRequest, (await keeper.GetAsync("/api/panel/reports?month=2026-13", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignIn("karim")).GetAsync("/api/panel/reports", Ct)).StatusCode);

        // the owner (2026-11-07): a day, a range or a year too, and the money of the last 12 months
        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var day = (await keeper.GetFromJsonAsync<JsonNode>($"/api/panel/reports?from={today}&to={today}", Ct))!;
        Assert.Equal((today, 2), ((string)day["month"]!, (int)day["new_clients"]!)); // both clients came today
        Assert.Equal(HttpStatusCode.BadRequest, (await keeper.GetAsync("/api/panel/reports?from=2026-11-07&to=2026-11-01", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await keeper.GetAsync("/api/panel/reports?from=2025-01-01&to=2026-11-07", Ct)).StatusCode); // over a year
        var trend = (await keeper.GetFromJsonAsync<JsonNode>("/api/panel/reports/trend", Ct))!["months"]!.AsArray();
        Assert.Equal(12, trend.Count);
        Assert.Equal(today[..7], (string)trend[^1]!["month"]!); // up to this month
    }
}
