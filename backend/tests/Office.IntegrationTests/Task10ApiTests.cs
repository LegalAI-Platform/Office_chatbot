using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Application.Panel;
using Office.Infrastructure.Services;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 10 (DECISIONS v1.14) through the real API and database: a written service from the client's documents to
/// the delivered file (B-16), the written opinion after a consultation (B-17), the price that holds 3 days, and
/// who may price (the lawyer and the manager). The app's clock stands at Thursday 19 November 2026, 12:00 Cairo.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class Task10ApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000093";
    private const string Password = "a long test password 1";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2));
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.service_files; DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments;
        DELETE FROM hazem.service_orders; DELETE FROM hazem.appointments;
        UPDATE hazem.settings SET value = '' WHERE key IN ('office.instapay', 'office.wallet_number', 'office.wallet_provider');
        UPDATE hazem.approved_texts SET active = true WHERE source IN ('proposal TASK 08', 'proposal TASK 09', 'proposal TASK 10') AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
        DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
        """;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync(Clean);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", Client);
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

    /// <summary>TASK 10's texts (approved, active since 0015) and the office's payment details.</summary>
    private Task ServicesOn() => db.ExecAsync("""
        UPDATE hazem.approved_texts SET active = true WHERE source = 'proposal TASK 10' AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
        UPDATE hazem.settings SET value = 'office@instapay' WHERE key = 'office.instapay';
        UPDATE hazem.settings SET value = '01000000000' WHERE key = 'office.wallet_number';
        UPDATE hazem.settings SET value = 'فودافون كاش' WHERE key = 'office.wallet_provider';
        """);

    private async Task<JsonArray> Turn(params JsonObject[] messages)
    {
        foreach (var m in messages)
        {
            m["wamid"] = $"wamid.T10-{++_n}";
        }

        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0010-{++_n:D12}", ["wa_id"] = Client, ["messages"] = new JsonArray(messages),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray();
    }

    private Task<JsonArray> Say(string text) => Turn(new JsonObject { ["type"] = "text", ["text"] = text });

    private static JsonObject File(string type, string mime, string sha) => new()
    {
        ["type"] = type,
        ["media"] = new JsonObject
        {
            ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = $"wa/2026/11/{sha}", ["mime_type"] = mime, ["size_bytes"] = 1000,
            ["sha256"] = sha, ["filename"] = "contract.pdf",
        },
    };

    private async Task<int> Notices()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ServiceNotices>().RunAsync(Ct);
    }

    private List<JsonNode> SentSince(int from) => _factory.N8n.Requests.Skip(from).Select(r => JsonNode.Parse(r.Body)!).ToList();

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

    private async Task<Guid> OrderWithDocuments()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        Assert.Contains("ابعتلي صورة العقد", (string?)(await Say("عايز حد يراجعلي عقد")).Single()!["text"]);
        var pages = await Turn(File("image", "image/jpeg", "p1"), File("document", "application/pdf", "p2"));
        Assert.StartsWith("استلمت ✅. الأستاذ هيراجع", (string?)Assert.Single(pages)!["text"]);
        return await db.ScalarAsync<Guid>("SELECT id FROM hazem.service_orders");
    }

    // ---------- B-16: from the request to the delivered file ----------

    [Fact]
    public async Task A_review_goes_from_the_documents_to_the_file_on_whatsapp()
    {
        await ServicesOn();
        var id = await OrderWithDocuments();
        Assert.Equal("awaiting_price|2|service_pricing", await db.ScalarAsync<string>("""
            SELECT o.state || '|' || (SELECT count(*) FROM hazem.service_files) || '|' || (SELECT string_agg(type, ',') FROM hazem.staff_tasks WHERE type = 'service_pricing')
              FROM hazem.service_orders o
            """));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.classifications WHERE turn_id = (SELECT max(turn_id) FROM hazem.turn_replies)"));

        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.Single((await staff.GetFromJsonAsync<JsonArray>("/api/panel/services", Ct))!);
        var price = new { price = 2500, exception = false, due_on = "2026-11-25", delivery = "file" };
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/panel/services/{id}/price", price, Ct)).StatusCode); // owner: b

        var lawyer = await SignIn("saad", Permissions.Lawyer);
        Assert.Equal("out_of_range", (string?)(await (await lawyer.PostAsJsonAsync($"/api/panel/services/{id}/price", price with { price = 5000 }, Ct))
            .Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]);
        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PostAsJsonAsync($"/api/panel/services/{id}/price", price, Ct)).StatusCode);
        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        var priced = (string)SentSince(sends).Single()["text"]!;
        Assert.StartsWith("💼 مراجعة العقد: السعر النهائي 2,500 جنيه", priced);
        Assert.Equal("awaiting_payment|requested|2500", await db.ScalarAsync<string>(
            "SELECT o.state || '|' || p.state || '|' || p.amount_due FROM hazem.service_orders o JOIN hazem.payments p ON p.service_order_id = o.id"));

        var receipt = await Turn(File("image", "image/jpeg", "receipt"));
        Assert.Contains("استلمنا صورة التحويل", (string?)receipt.Single()!["text"]);
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review", new { state = "verified", received = 2500 }, Ct)).StatusCode);
        Assert.Equal("in_progress", await db.ScalarAsync<string>("SELECT state FROM hazem.service_orders"));
        sends = _factory.N8n.Requests.Count;
        await Notices();
        Assert.Contains("الأستاذ بدأ", (string?)SentSince(sends).Single()["text"]);

        var up = (await (await staff.PostAsJsonAsync($"/api/panel/services/{id}/upload", new { filename = "مراجعة العقد.pdf" }, Ct))
            .Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.StartsWith("/storage/office-sensitive/deliverables/", (string?)up["url"]);
        var key = (string)up["key"]!;
        Assert.Equal("no_file", (string?)(await (await staff.PostAsJsonAsync($"/api/panel/services/{id}/deliver", new { key, filename = "مراجعة العقد.pdf" }, Ct))
            .Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]); // not uploaded yet
        _factory.Deliverables.Objects[key] = 1234;
        Assert.Equal("invalid", (string?)(await (await staff.PostAsJsonAsync($"/api/panel/services/{id}/deliver", new { key, filename = "مراجعة العقد.docx" }, Ct))
            .Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]); // a PDF uploaded is never sent as Word
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/services/{id}/deliver", new { key, filename = "مراجعة العقد.pdf" }, Ct)).StatusCode);

        sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        var file = SentSince(sends).Single();
        Assert.Equal(("send_document", "مراجعة العقد.pdf"), ((string?)file["type"], (string?)file["document"]!["filename"]));
        Assert.StartsWith("https://storage.test/office-sensitive/deliverables/", (string?)file["document"]!["link"]);
        Assert.Equal("delivered|sent", await db.ScalarAsync<string>("SELECT state || '|' || client_notice FROM hazem.service_orders"));

        // The 24-hour window: «ابعت الملف تاني» from the panel, and the send that failed goes again when the client writes.
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsync($"/api/panel/services/{id}/resend", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsync($"/api/panel/services/{id}/resend", null, Ct)).StatusCode); // already waiting
        sends = _factory.N8n.Requests.Count;
        await Notices();
        Assert.EndsWith(":1:0", (string?)SentSince(sends).Single()["idempotency_key"]);
        await db.ExecAsync("UPDATE hazem.service_orders SET client_notice = 'failed'"); // as if outside the window
        await Say("شكرا");
        sends = _factory.N8n.Requests.Count;
        await Notices();
        Assert.Equal("send_document", (string?)SentSince(sends).Single()["type"]);
        Assert.Equal("service.priced|service.delivered|service.resent", await db.ScalarAsync<string>(
            "SELECT string_agg(action, '|' ORDER BY id) FROM hazem.panel_audit WHERE target_type = 'service'"));
    }

    [Fact]
    public async Task The_price_holds_three_days_then_the_order_closes_with_a_task()
    {
        await ServicesOn();
        var id = await OrderWithDocuments();
        var lawyer = await SignIn("saad", Permissions.Lawyer);
        await lawyer.PostAsJsonAsync($"/api/panel/services/{id}/price", new { price = 1500, exception = false, due_on = "2026-11-25", delivery = "call" }, Ct);
        await Notices();
        await db.ExecAsync("UPDATE hazem.service_orders SET price_until = now() + interval '20 hours'");
        var sends = _factory.N8n.Requests.Count;
        await Notices();
        Assert.StartsWith("⏰ تذكير: سعر مراجعة العقد (1,500 جنيه)", (string?)SentSince(sends).Single()["text"]);

        await db.ExecAsync("UPDATE hazem.service_orders SET price_until = now() - interval '1 minute'");
        sends = _factory.N8n.Requests.Count;
        await Notices();
        Assert.StartsWith("طلب مراجعة العقد اتقفل لأن التحويل", (string?)SentSince(sends).Single()["text"]);
        Assert.Equal("closed|price_expired|service_unpaid", await db.ScalarAsync<string>("""
            SELECT state || '|' || closed_reason || '|' || (SELECT type FROM hazem.staff_tasks WHERE type = 'service_unpaid') FROM hazem.service_orders
            """));
    }

    [Fact]
    public async Task Closing_before_the_review_promises_no_refund_until_the_money_is_found()
    {
        await ServicesOn();
        var id = await OrderWithDocuments();
        var lawyer = await SignIn("saad", Permissions.Lawyer);
        await lawyer.PostAsJsonAsync($"/api/panel/services/{id}/price", new { price = 1500, exception = false, due_on = "2026-11-25", delivery = "call" }, Ct);
        await Turn(File("image", "image/jpeg", "r1"));
        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PostAsync($"/api/panel/services/{id}/close", null, Ct)).StatusCode);
        Assert.Equal("closed|proof_received|0", await db.ScalarAsync<string>("""
            SELECT o.state || '|' || p.state || '|' || (SELECT count(*) FROM hazem.staff_tasks WHERE type = 'refund')
              FROM hazem.service_orders o JOIN hazem.payments p ON p.service_order_id = o.id
            """)); // nobody checked the receipt yet: no refund promised
        Assert.Equal(HttpStatusCode.Conflict, (await lawyer.PostAsync($"/api/panel/services/{id}/close", null, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PostAsJsonAsync($"/api/panel/payments/{id}/review", new { state = "verified", received = 1500 }, Ct)).StatusCode);
        Assert.Equal("closed|refund_requested|1500|office_cancelled|1", await db.ScalarAsync<string>("""
            SELECT o.state || '|' || p.state || '|' || p.refund_due || '|' || p.refund_reason || '|' || (SELECT count(*) FROM hazem.staff_tasks WHERE type = 'refund')
              FROM hazem.service_orders o JOIN hazem.payments p ON p.service_order_id = o.id
            """)); // the money came for an order the office closed: it goes back
    }

    [Fact]
    public async Task A_paid_order_the_office_closes_is_refunded_and_a_call_delivery_tells_the_client()
    {
        await ServicesOn();
        var id = await OrderWithDocuments();
        var lawyer = await SignIn("saad", Permissions.Lawyer);
        await lawyer.PostAsJsonAsync($"/api/panel/services/{id}/price", new { price = 1500, exception = false, due_on = "2026-11-25", delivery = "call" }, Ct);
        await Turn(File("image", "image/jpeg", "r1"));
        await lawyer.PostAsJsonAsync($"/api/panel/payments/{id}/review", new { state = "verified", received = 1500 }, Ct);
        await Notices(); // «الأستاذ بدأ»
        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PostAsJsonAsync($"/api/panel/services/{id}/deliver", new { }, Ct)).StatusCode);
        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        var told = SentSince(sends).Single();
        Assert.Equal(("send_text", "📄 مراجعة العقد بتاع حضرتك جاهز ✅"), ((string?)told["type"], (string?)told["text"])); // after the call: words, no file

        var month = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1500, (int)(await lawyer.GetFromJsonAsync<JsonNode>($"/api/panel/reports?month={month}", Ct))!["received"]!); // the report counts it
    }

    [Fact]
    public async Task Part_of_the_money_keeps_the_order_for_the_office_and_not_received_closes_it()
    {
        await ServicesOn();
        var id = await OrderWithDocuments();
        var lawyer = await SignIn("saad", Permissions.Lawyer);
        await lawyer.PostAsJsonAsync($"/api/panel/services/{id}/price", new { price = 2000, exception = false, due_on = "2026-11-25", delivery = "file" }, Ct);
        await Turn(File("image", "image/jpeg", "r1"));
        await lawyer.PostAsJsonAsync($"/api/panel/payments/{id}/review", new { state = "partial", received = 500 }, Ct);
        await db.ExecAsync("UPDATE hazem.service_orders SET price_until = now() - interval '1 minute'");
        await Notices();
        await Notices();
        Assert.Equal("awaiting_payment|true|1", await db.ScalarAsync<string>("""
            SELECT state || '|' || (price_expired_at IS NOT NULL) || '|' || (SELECT count(*) FROM hazem.staff_tasks WHERE type = 'service_unpaid')
              FROM hazem.service_orders
            """)); // the office decides (a task, once); the client is not told it closed

        await lawyer.PostAsJsonAsync($"/api/panel/payments/{id}/review", new { state = "not_received" }, Ct);
        await Notices();
        Assert.Equal("closed|price_expired", await db.ScalarAsync<string>("SELECT state || '|' || closed_reason FROM hazem.service_orders"));
    }

    // ---------- B-17 ----------

    [Fact]
    public async Task A_written_opinion_after_a_consultation_asks_for_the_2000()
    {
        await ServicesOn();
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        var appointment = await db.ScalarAsync<Guid>("""
            INSERT INTO hazem.appointments (id, person_id, service, starts_at, ends_at, amount, booked_turn_id, state, calendar_state)
            SELECT gen_random_uuid(), id, 'consultation_phone_video', '2026-11-18 12:00+02', '2026-11-18 12:15+02', 500, 't-done', 'completed', 'synced'
              FROM hazem.persons RETURNING id
            """);
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        var body = new { due_on = "2026-11-26", delivery = "file" };
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/panel/booking/appointments/{appointment}/opinion", body, Ct)).StatusCode);
        var manager = await SignIn("boss", Permissions.Manager);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync($"/api/panel/booking/appointments/{appointment}/opinion", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync($"/api/panel/booking/appointments/{appointment}/opinion", body, Ct)).StatusCode);
        Assert.Equal("written_opinion|awaiting_payment|2000", await db.ScalarAsync<string>(
            "SELECT o.kind || '|' || o.state || '|' || p.amount_due FROM hazem.service_orders o JOIN hazem.payments p ON p.service_order_id = o.id"));
        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        Assert.StartsWith("💼 الرأي القانوني المكتوب: السعر النهائي 2,000 جنيه", (string?)SentSince(sends).Single()["text"]);
    }

    [Fact]
    public async Task Without_the_texts_a_named_service_is_answered_as_before_and_opens_no_order()
    {
        await db.ExecAsync("UPDATE hazem.approved_texts SET active = false WHERE source = 'proposal TASK 10'"); // approved since 0015: switched off here
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        await Say("عايز حد يراجعلي عقد");
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.service_orders"));
        Assert.Equal("service_request", await db.ScalarAsync<string>("SELECT type FROM hazem.staff_tasks"));
    }
}
