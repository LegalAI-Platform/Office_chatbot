using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Application.Panel;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 06 (DECISIONS v1.17, B-20 / B-23) through the real API and database: the price list in the panel (who may change it,
/// its history), a new price asked for and quoted while what was booked keeps its amount, the SQL rules reading the list, and
/// the office's questions answered from the panel. The app's clock stands at Thursday 19 November 2026, 12:00 Cairo.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class Task06ApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string First = "201000000096";
    private const string Second = "201000000097";
    private const string Password = "a long test password 1";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2));
    private static string At(int hour) => "bk:s:pv:" + new DateTimeOffset(2026, 11, 20, hour, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds();
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments; DELETE FROM hazem.service_files; DELETE FROM hazem.service_orders;
        DELETE FROM hazem.appointment_reminders; DELETE FROM hazem.appointments;
        UPDATE hazem.settings SET value = 'false' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = '' WHERE key IN ('booking.calendar_id', 'office.instapay', 'office.wallet_number', 'office.wallet_provider',
                                                          'office.address', 'office.map_url');
        UPDATE hazem.settings SET value = '500' WHERE key = 'price.phone_video';
        UPDATE hazem.settings SET value = '1000' WHERE key = 'price.office';
        UPDATE hazem.settings SET value = '3000' WHERE key = 'price.review_max';
        UPDATE hazem.approved_texts SET active = true
         WHERE source IN ('proposal TASK 08', 'proposal TASK 09', 'proposal TASK 06') AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
        DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
        """;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync(Clean);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", $"{First},{Second}");
        _factory = new ApiFactory(db, new FixedClock(Now));
        _n8n = _factory.CreateN8nClient();
    }

    public async ValueTask DisposeAsync()
    {
        _n8n.Dispose();
        await _factory.DisposeAsync();
        await db.ExecAsync(Clean);
        await db.SetAsync("hazem.recipients", "");
    }

    private Task BookingOn() => db.ExecAsync("""
        UPDATE hazem.approved_texts SET active = true WHERE source IN ('proposal TASK 08', 'proposal TASK 09') AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
        UPDATE hazem.settings SET value = 'true' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = 'lawyer@example.test' WHERE key = 'booking.calendar_id';
        UPDATE hazem.settings SET value = 'office@instapay' WHERE key = 'office.instapay';
        UPDATE hazem.settings SET value = '01000000000' WHERE key = 'office.wallet_number';
        """);

    private async Task<JsonArray> Turn(string waId, JsonObject message)
    {
        message["wamid"] = $"wamid.T06-{++_n}";
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0006-{++_n:D12}", ["wa_id"] = waId, ["messages"] = new JsonArray(message),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray();
    }

    private Task<JsonArray> Say(string waId, string text) => Turn(waId, new JsonObject { ["type"] = "text", ["text"] = text });

    private Task<JsonArray> Press(string waId, string id) =>
        Turn(waId, new JsonObject { ["type"] = "interactive", ["interactive"] = new JsonObject { ["kind"] = "list_reply", ["id"] = id, ["title"] = "x" } });

    private static string Text(JsonArray actions) => string.Join(" || ", actions.Select(a => (string?)a!["text"]));

    private async Task Named(string waId)
    {
        await Say(waId, "السلام عليكم");
        await Say(waId, "محمد أحمد علي حسن");
    }

    private async Task<HttpClient> SignIn(string name, string role, string? staff = null)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PanelAccounts>().CreateAsync(null,
                new PanelAccounts.NewAccount(name, name, $"{name}@example.test", role, staff), Password, Ct);
        }

        var c = _factory.CreateClient();
        await Csrf(c);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/panel/auth/login", new { username = name, password = Password }, Ct)).StatusCode);
        await Csrf(c);
        return c;
    }

    private static async Task Csrf(HttpClient c)
    {
        var body = await c.GetFromJsonAsync<JsonNode>("/api/panel/auth/csrf", Ct);
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (string)body!["token"]!);
    }

    /// <summary>The whole list as the panel sends it, with some lines changed.</summary>
    private static async Task<Dictionary<string, int>> ListWith(HttpClient c, Dictionary<string, int> changes)
    {
        var list = (await c.GetFromJsonAsync<JsonArray>("/api/panel/prices", Ct))!;
        var values = list.ToDictionary(p => (string)p!["key"]!, p => (int)p!["value"]!);
        foreach (var (k, v) in changes)
        {
            values[k] = v;
        }

        return values;
    }

    // ---------- B-23: the list in the panel ----------

    [Fact]
    public async Task Everyone_reads_the_list_and_only_the_lawyer_or_the_manager_changes_it_with_its_history()
    {
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        var list = (await staff.GetFromJsonAsync<JsonArray>("/api/panel/prices", Ct))!;
        Assert.Equal(10, list.Count);
        Assert.Equal(500, (int)list.Single(p => (string)p!["key"]! == "price.phone_video")!["value"]!);
        var changed = await ListWith(staff, new() { ["price.phone_video"] = 600 });
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/panel/prices", new { values = changed }, Ct)).StatusCode);

        var lawyer = await SignIn("saad", Permissions.Lawyer);
        var upsideDown = await ListWith(lawyer, new() { ["price.review_min"] = 4000 });
        Assert.Equal("review_range", (string?)(await (await lawyer.PutAsJsonAsync("/api/panel/prices", new { values = upsideDown }, Ct))
            .Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]);
        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PutAsJsonAsync("/api/panel/prices", new { values = changed }, Ct)).StatusCode);

        var history = (await lawyer.GetFromJsonAsync<JsonArray>("/api/panel/prices/changes", Ct))!;
        var only = Assert.Single(history)!;
        Assert.Equal(("price.phone_video", "500", "600", "saad"), ((string?)only["key"], (string?)only["from"], (string?)only["to"], (string?)only["by"]));
        Assert.Equal("600", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'price.phone_video'"));
    }

    [Fact]
    public async Task Two_editors_change_different_lines_and_neither_undoes_the_other()
    {
        var lawyer = await SignIn("saad", Permissions.Lawyer);
        var manager = await SignIn("mona", Permissions.Manager);
        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PutAsJsonAsync("/api/panel/prices",
            new { values = new Dictionary<string, int> { ["price.phone_video"] = 650 } }, Ct)).StatusCode);
        // The manager's page was opened before: only the line she changed is sent, so 650 stays.
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync("/api/panel/prices",
            new { values = new Dictionary<string, int> { ["price.office"] = 1200 } }, Ct)).StatusCode);
        Assert.Equal("650|1200", await db.ScalarAsync<string>("""
            SELECT (SELECT value FROM hazem.settings WHERE key = 'price.phone_video') || '|' || (SELECT value FROM hazem.settings WHERE key = 'price.office')
            """));
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT count(*)::int FROM hazem.panel_audit WHERE action = 'price.changed'"));

        // A line that does not exist, or a change that turns the review's range upside down with the line kept: refused.
        async Task<string?> Error(Dictionary<string, int> values) => (string?)(await (await manager.PutAsJsonAsync("/api/panel/prices",
            new { values }, Ct)).Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"];
        Assert.Equal("invalid_price", await Error(new() { ["price.unknown"] = 100 }));
        Assert.Equal("review_range", await Error(new() { ["price.review_min"] = 3500 }));
    }

    [Fact]
    public async Task A_new_price_is_quoted_and_asked_for_while_an_earlier_hold_keeps_its_amount()
    {
        await BookingOn();
        await Named(First);
        Assert.StartsWith("⏳", Text(await Press(First, At(12))));
        Assert.Equal(500, await db.ScalarAsync<int>("SELECT amount FROM hazem.appointments"));

        var lawyer = await SignIn("saad", Permissions.Lawyer);
        await lawyer.PutAsJsonAsync("/api/panel/prices", new { values = await ListWith(lawyer, new() { ["price.phone_video"] = 600 }) }, Ct);

        await Named(Second);
        Assert.Contains("بالتليفون بـ600 أو في المكتب بـ1,000", Text(await Say(Second, "عايز أحجز")));
        Assert.Contains("600 جنيه", Text(await Press(Second, At(14))));
        Assert.Equal("500|600", await db.ScalarAsync<string>(
            "SELECT string_agg(amount::text, '|' ORDER BY starts_at) FROM hazem.appointments")); // the first hold keeps 500
    }

    [Fact]
    public async Task The_pricing_range_and_the_written_opinion_read_the_list()
    {
        await db.ExecAsync($"""
            INSERT INTO hazem.persons (id, wa_id, first_seen_at) VALUES ('00000000-0000-0000-0006-000000000001', '{First}', now());
            INSERT INTO hazem.service_orders (id, person_id, kind, state, opened_key)
            VALUES ('00000000-0000-0000-0006-000000000002', '00000000-0000-0000-0006-000000000001', 'document_review', 'awaiting_price', 't06');
            """);
        const string price = "SELECT hazem.price_service_order('00000000-0000-0000-0006-000000000002', 3500, false, current_date + 3, 'file', NULL) ->> 'status'";
        Assert.Equal("out_of_range", await db.ScalarAsync<string>(price));
        await db.ExecAsync("UPDATE hazem.settings SET value = '4000' WHERE key = 'price.review_max'");
        Assert.Equal("priced", await db.ScalarAsync<string>(price));
        Assert.Equal(2000, await db.ScalarAsync<int>("SELECT hazem.price('price.written_opinion', 1)"));
        await db.ExecAsync("UPDATE hazem.settings SET value = 'broken' WHERE key = 'price.review_max'");
        Assert.Equal(3000, await db.ScalarAsync<int>("SELECT hazem.price('price.review_max', 3000)")); // a broken value is B-23's
        await db.ExecAsync("UPDATE hazem.settings SET value = '2000000' WHERE key = 'price.review_max'");
        Assert.Equal(3000, await db.ScalarAsync<int>("SELECT hazem.price('price.review_max', 3000)")); // over 1,000,000: as PriceList.Valid
        await db.ExecAsync("UPDATE hazem.settings SET value = '1000000' WHERE key = 'price.review_max'");
        Assert.Equal(1000000, await db.ScalarAsync<int>("SELECT hazem.price('price.review_max', 3000)"));
    }

    // ---------- B-20 (1.17): the office's questions ----------

    [Fact]
    public async Task The_address_is_answered_from_the_panel_and_a_missing_one_gives_the_office_a_task()
    {
        await db.ExecAsync("UPDATE hazem.approved_texts SET active = true WHERE source = 'proposal TASK 06' AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key)");
        await Named(First);
        var none = await Say(First, "المكتب فين؟");
        Assert.StartsWith("الأستاذ يقدر يشوف الموضوع", Text(none)); // never half a message: the offer alone
        Assert.Equal("office_info|address|1", await db.ScalarAsync<string>("""
            SELECT (SELECT intent || '|' || info_topic FROM hazem.classifications ORDER BY created_at DESC LIMIT 1) || '|' ||
                   (SELECT count(*) FROM hazem.staff_tasks WHERE type = 'office_info_missing')
            """));
        // A second client asks before it is written: still one task for the office, not one per question.
        await Named(Second);
        await Say(Second, "المكتب فين؟");
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*)::int FROM hazem.staff_tasks WHERE type = 'office_info_missing'"));

        await db.ExecAsync("""
            UPDATE hazem.settings SET value = '183 شارع التحرير، وسط البلد، القاهرة' WHERE key = 'office.address';
            UPDATE hazem.settings SET value = 'https://maps.example.test/office' WHERE key = 'office.map_url';
            """);
        var d = await Say(First, "المكتب فين؟");
        Assert.StartsWith("📍 عنوان المكتب: 183 شارع التحرير، وسط البلد، القاهرة.\nالخريطة: https://maps.example.test/office", Text(d));
        Assert.Contains("الأستاذ يقدر يشوف الموضوع", Text(d));
    }

    [Fact]
    public async Task The_price_list_answer_quotes_the_panel_s_prices()
    {
        await db.ExecAsync("""
            UPDATE hazem.approved_texts SET active = true WHERE source = 'proposal TASK 06' AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
            UPDATE hazem.settings SET value = '700' WHERE key = 'price.phone_video';
            """);
        await Named(First);
        var d = Text(await Say(First, "الأسعار إيه؟"));
        Assert.Contains("استشارة بالتليفون أو الفيديو (ربع ساعة): 700 جنيه", d);
        Assert.Contains("جواز أو طلاق عرفي: 5,000 جنيه (شهود المكتب) أو 4,000 (شهود حضرتك)", d);
        Assert.DoesNotContain("{{", d);
    }
}
