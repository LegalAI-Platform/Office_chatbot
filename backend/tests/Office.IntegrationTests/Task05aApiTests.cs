using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Office.Infrastructure.Lists;

namespace Office.IntegrationTests;

/// <summary>TASK 05a through the real API and database: lists, existing clients, source, staff events.</summary>
[Collection(nameof(HazemApiTests))]
public sealed class Task05aApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000081";
    private const string Listed = "201000000082";
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync("""
            TRUNCATE hazem.turns, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.staff_tasks, hazem.list_entries;
            DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
            """);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", $"{Client},{Listed}");
        _factory = new ApiFactory(db);
        _n8n = _factory.CreateN8nClient();
    }

    public async ValueTask DisposeAsync()
    {
        _n8n.Dispose();
        await _factory.DisposeAsync();
        await db.SetAsync("hazem.recipients", "");
    }

    private async Task<ListImporter.Result> Import(string kind, string csv, bool replace = false)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ListImporter>().ImportAsync(kind, new StringReader(csv), replace, Ct);
    }

    private async Task<List<string?>> Say(string text, string waId = Client, JsonObject? referral = null)
    {
        var message = new JsonObject { ["wamid"] = $"wamid.T05-{++_n}", ["type"] = "text", ["text"] = text };
        if (referral is not null)
        {
            message["referral"] = referral;
        }

        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0005-{++_n:D12}", ["wa_id"] = waId, ["messages"] = new JsonArray(message),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        return body["actions"]!.AsArray().Select(a => (string?)a!["text"]).ToList();
    }

    // ---------- import (B-14) ----------

    [Fact]
    public async Task Lists_import_from_csv_with_every_phone_shape_and_bad_lines_never_show_the_number()
    {
        var r = await Import("existing_client", """
            name,phone,staff
            محمد أحمد علي حسن,010 1234 5678,أ. محمود
            "منى سعيد, عبد الله",+20 11 2222 3333,أ. محمود
            سارة,٠١٢٣٤٥٦٧٨٩٠,
            غلط,01999-ABC-777,
            """);
        Assert.Equal(3, r.Imported);
        var bad = Assert.Single(r.Rejected);
        Assert.Equal(5, bad.Line);
        Assert.DoesNotContain("01999", bad.Reason);
        Assert.Equal("201012345678|201122223333|201234567890|1", await db.ScalarAsync<string>("""
            SELECT string_agg(wa_id, '|' ORDER BY wa_id) || '|' || (SELECT count(*) FROM hazem.staff)
              FROM hazem.list_entries WHERE list_kind = 'existing_client'
            """));
    }

    [Fact]
    public async Task Replace_swaps_a_whole_list()
    {
        await Import("blocked", "name,phone\nأ,01011111111\nب,01022222222\n");
        await Import("blocked", "name,phone\nج,01033333333\n", replace: true);
        Assert.Equal("201033333333", await db.ScalarAsync<string>("SELECT string_agg(wa_id, ',') FROM hazem.list_entries WHERE list_kind = 'blocked'"));
    }

    [Theory]
    [InlineData("name,phone\nج,01033333333\nغلط,0103\n")] // one bad line
    [InlineData("name,phone\nغلط,abc\n")]                // no good line at all
    [InlineData("name,telephone\nج,01033333333\n")]      // no phone column
    public async Task Replace_with_a_bad_file_changes_nothing(string csv)
    {
        await Import("blocked", "name,phone\nأ,01011111111\nب,01022222222\n");
        var r = await Import("blocked", csv, replace: true);
        Assert.Equal(0, r.Imported);
        Assert.NotEmpty(r.Rejected);
        Assert.Equal("201011111111,201022222222", await db.ScalarAsync<string>(
            "SELECT string_agg(wa_id, ',' ORDER BY wa_id) FROM hazem.list_entries WHERE list_kind = 'blocked'"));
    }

    [Fact]
    public async Task A_quoted_cell_with_a_line_break_stays_one_row()
    {
        var r = await Import("existing_client", "name,phone,staff\n\"محمد أحمد\nعلي حسن\",01044444444,أ. محمود\nسارة,01055555555,\n");
        Assert.Equal((2, 0), (r.Imported, r.Rejected.Count));
        Assert.Equal("محمد أحمد\nعلي حسن", await db.ScalarAsync<string>(
            "SELECT name FROM hazem.list_entries WHERE wa_id = '201044444444'"));
    }

    [Fact]
    public async Task Tasks_overdue_marks_only_open_tasks_past_their_due_time()
    {
        await Say("السلام عليكم");
        await db.ExecAsync($"""
            INSERT INTO hazem.staff_tasks (person_id, turn_id, type, state, due_at)
            SELECT p.id, t.turn_id, 'complaint', t.state, t.due_at
              FROM hazem.persons p,
                   (VALUES ('a', 'open', now() - interval '1 hour'), ('b', 'in_progress', now() - interval '1 hour'),
                           ('c', 'done', now() - interval '1 hour'), ('d', 'open', now() + interval '1 hour'))
                   AS t(turn_id, state, due_at)
             WHERE p.wa_id = '{Client}'
            """);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<TaskMaintenance>().MarkOverdueAsync(Ct));
        Assert.Equal("a:overdue,b:overdue,c:done,d:open", await db.ScalarAsync<string>(
            "SELECT string_agg(turn_id || ':' || state, ',' ORDER BY turn_id) FROM hazem.staff_tasks WHERE type = 'complaint'"));
    }

    // ---------- the flow on real data ----------

    [Fact]
    public async Task A_listed_client_asking_about_their_case_gets_the_neutral_text_and_a_task_for_their_staff()
    {
        await Import("existing_client", $"name,phone,staff\nمحمد أحمد علي حسن,{Listed},أ. محمود\n");
        Assert.Equal(["رسالة حضرتك وصلت للمكتب، وهتتبلّغ للمسؤول عن موضوعك."], await Say("الجلسة الجاية امتى؟", Listed));
        Assert.Equal("existing_client_message|open|أ. محمود|true", await db.ScalarAsync<string>("""
            SELECT t.type || '|' || t.state || '|' || s.name || '|' || (t.due_at > t.created_at)
              FROM hazem.staff_tasks t JOIN hazem.staff s ON s.id = t.staff_id
            """));
        Assert.Equal("existing_client|true", await db.ScalarAsync<string>(
            $"SELECT i.client_type || '|' || p.full_name_provided FROM hazem.intakes i JOIN hazem.persons p ON p.id = i.person_id WHERE p.wa_id = '{Listed}'"));
    }

    [Fact]
    public async Task A_blocked_number_gets_nothing_and_its_conversation_is_muted()
    {
        await Import("blocked", $"name,phone\nx,{Client}\n");
        Assert.Empty(await Say("السلام عليكم"));
        Assert.Equal("muted", await db.ScalarAsync<string>(
            $"SELECT c.state FROM hazem.conversations c JOIN hazem.persons p ON p.id = c.person_id WHERE p.wa_id = '{Client}'"));
        Assert.Empty(_factory.OpenAi.Requests);
    }

    [Fact]
    public async Task An_ad_referral_reaches_the_api_and_is_recorded_as_the_source()
    {
        await Say("السلام عليكم", referral: new JsonObject { ["source_type"] = "ad", ["source_id"] = "120208", ["headline"] = "استشارة قانونية" });
        Assert.Equal("ad|120208", await db.ScalarAsync<string>(
            $"SELECT source || '|' || (source_raw->>'source_id') FROM hazem.persons WHERE wa_id = '{Client}'"));
    }

    [Fact]
    public async Task A_referral_with_a_link_in_its_id_is_refused()
    {
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = "wt:00000000-0000-0000-0005-999999999999", ["wa_id"] = Client,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["wamid"] = "wamid.T05-r", ["type"] = "text", ["text"] = "x",
                ["referral"] = new JsonObject { ["source_type"] = "ad", ["source_id"] = "https://fb.me/x" },
            }),
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ---------- staff events (B-12) ----------

    private Task<HttpResponseMessage> StaffEvent(string evt, string waId = Client) =>
        _n8n.PostAsJsonAsync("/api/hazem/staff-events", new { wa_id = waId, @event = evt }, Ct);

    [Fact]
    public async Task Staff_events_silence_hazem_and_hand_back()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await StaffEvent("staff_replied")).StatusCode);
        await Say("السلام عليكم");
        var r = await StaffEvent("staff_replied");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("human_handoff", (string?)(await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["state"]);
        Assert.Empty(await Say("محمد أحمد علي حسن"));

        Assert.Equal(HttpStatusCode.OK, (await StaffEvent("hand_back")).StatusCode);
        Assert.NotEmpty(await Say("محمد أحمد علي حسن"));
        Assert.Equal(HttpStatusCode.Conflict, (await StaffEvent("hand_back")).StatusCode); // already with Hazem
    }

    [Fact]
    public async Task Staff_events_need_the_token_and_a_known_event()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/hazem/staff-events", new { wa_id = Client, @event = "hand_back" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await StaffEvent("delete_everything")).StatusCode);
    }

    [Fact]
    public async Task A_staff_event_in_the_future_is_refused()
    {
        await Say("السلام عليكم");
        var future = await _n8n.PostAsJsonAsync("/api/hazem/staff-events",
            new { wa_id = Client, @event = "staff_replied", at = DateTimeOffset.UtcNow.AddDays(60) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Equal("bot_active", await db.ScalarAsync<string>(
            $"SELECT c.state FROM hazem.conversations c JOIN hazem.persons p ON p.id = c.person_id WHERE p.wa_id = '{Client}'"));
        var lateReport = await _n8n.PostAsJsonAsync("/api/hazem/staff-events",
            new { wa_id = Client, @event = "staff_replied", at = DateTimeOffset.UtcNow.AddMinutes(-30) }, Ct);
        Assert.Equal(HttpStatusCode.OK, lateReport.StatusCode);
    }

    // ---------- migration 0008 ----------

    [Theory]
    [InlineData("list_entries")]
    [InlineData("staff_tasks")]
    [InlineData("staff")]
    public async Task Office_app_is_blocked_and_readonly_reads_the_new_tables(string table)
    {
        await using (var app = new NpgsqlConnection(db.AppConnectionString))
        {
            await app.OpenAsync(Ct);
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM hazem.{table}", app);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync(Ct))).SqlState);
        }

        Assert.True(await db.ScalarAsync<bool>(
            $"SELECT has_table_privilege('office_readonly', 'hazem.{table}', 'SELECT') AND NOT has_table_privilege('office_readonly', 'hazem.{table}', 'INSERT')"));
    }

    [Fact]
    public async Task Handoff_stays_off_and_no_active_text_promises_a_contact_time()
    {
        Assert.Equal("false", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'handoff.enabled'"));
        Assert.Equal(0L, await db.ScalarAsync<long>(
            "SELECT count(*) FROM hazem.approved_texts WHERE active AND text ~ '(خلال ساعة|الساعة 10|هيتواصل|هيرد عليك)'"));
    }
}
