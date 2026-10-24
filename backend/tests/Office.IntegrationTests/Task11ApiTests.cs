using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Application.Panel;
using Office.Application.Urf;
using Office.Infrastructure.Urf;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 11 (DECISIONS v1.15, B-11 / S-06) through the real API and database: the Urfi contract from the questions to the
/// lawyer's review (approved: the confirmation with who comes; refused: cancelled by the office and paid back), the
/// 24-hour documents task, and who may review. The app's clock stands at Thursday 19 November 2026, 12:00 Cairo.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class Task11ApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000094";
    private const string Password = "a long test password 1";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly string Tomorrow12 = "bk:s:mo:" + new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds();
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.urf_files; DELETE FROM hazem.urf_cases;
        DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments; DELETE FROM hazem.service_files; DELETE FROM hazem.service_orders;
        DELETE FROM hazem.appointments;
        UPDATE hazem.settings SET value = 'false' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = '' WHERE key IN ('booking.calendar_id', 'office.instapay', 'office.wallet_number', 'office.wallet_provider');
        UPDATE hazem.approved_texts SET active = true
         WHERE source IN ('proposal TASK 08', 'proposal TASK 09', 'proposal TASK 10', 'proposal TASK 11');
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

    /// <summary>The booking on (as TASK 09's tests); TASK 11's texts are approved (active since 0016).</summary>
    private Task UrfOn() => db.ExecAsync("""
        UPDATE hazem.approved_texts SET active = true WHERE source IN ('proposal TASK 09', 'proposal TASK 11');
        UPDATE hazem.settings SET value = 'true' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = 'lawyer@example.test' WHERE key = 'booking.calendar_id';
        UPDATE hazem.settings SET value = 'office@instapay' WHERE key = 'office.instapay';
        UPDATE hazem.settings SET value = '01000000000' WHERE key = 'office.wallet_number';
        UPDATE hazem.settings SET value = 'فودافون كاش' WHERE key = 'office.wallet_provider';
        """);

    private async Task<JsonArray> Turn(params JsonObject[] messages)
    {
        foreach (var m in messages)
        {
            m["wamid"] = $"wamid.T11-{++_n}";
        }

        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0011-{++_n:D12}", ["wa_id"] = Client, ["messages"] = new JsonArray(messages),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray();
    }

    private Task<JsonArray> Say(string text) => Turn(new JsonObject { ["type"] = "text", ["text"] = text });

    private Task<JsonArray> Press(string id) =>
        Turn(new JsonObject { ["type"] = "interactive", ["interactive"] = new JsonObject { ["kind"] = "button_reply", ["id"] = id, ["title"] = "x" } });

    private static JsonObject Image(string sha) => new()
    {
        ["type"] = "image",
        ["media"] = new JsonObject
        {
            ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = $"wa/2026/11/{sha}.jpg", ["mime_type"] = "image/jpeg",
            ["size_bytes"] = 1000, ["sha256"] = sha,
        },
    };

    private static string Text(JsonArray actions) => string.Join(" || ", actions.Select(a => (string?)a!["text"]));

    private async Task<int> Notices()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UrfNotices>().RunAsync(Ct);
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

    /// <summary>B-11 up to the documents: the questions, the hold at 12:00 tomorrow (office witnesses, 5,000), the receipt, two IDs.</summary>
    private async Task<Guid> BookedWithDocuments()
    {
        await UrfOn();
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        Assert.Equal("سن الزوج كام، وسن الزوجة كام؟", Text(await Say("عايز أعمل جواز عرفي")));
        Assert.StartsWith("دي أول جوازة للزوجة", Text(await Say("30 و 25")));
        await Press(UrfAnswer.Id(UrfStep.WifePast, "first"));
        await Press(UrfAnswer.Id(UrfStep.Guardian, "father"));
        var times = await Press(UrfAnswer.Id(UrfStep.Witnesses, "office"));
        Assert.Contains("5,000", Text(times));
        Assert.Contains("بطاقة الولي (الأب)", Text(times));
        Assert.StartsWith("⏳", Text(await Press(Tomorrow12)));
        Assert.Equal("urf_marriage_office|5000|60|booked", await db.ScalarAsync<string>("""
            SELECT a.service || '|' || a.amount || '|' || extract(epoch FROM a.ends_at - a.starts_at)::int / 60 || '|' || c.state
              FROM hazem.urf_cases c JOIN hazem.appointments a ON a.id = c.appointment_id
            """));

        var paid = await Turn(Image("receipt"));
        Assert.Contains("ابعت هنا صور المستندات", Text(paid));
        var ids = await Turn(Image("id-1"), Image("id-2"));
        Assert.StartsWith("استلمت ✅. الأستاذ هيراجع الورق", Text(ids));
        Assert.Equal("2|urf_review|0", await db.ScalarAsync<string>("""
            SELECT (SELECT count(*) FROM hazem.urf_files) || '|' || (SELECT string_agg(type, ',') FROM hazem.staff_tasks WHERE type = 'urf_review')
                   || '|' || (SELECT count(*) FROM hazem.classifications c JOIN hazem.turns t ON t.turn_id = c.turn_id
                               WHERE t.turn_id = (SELECT max(turn_id) FROM hazem.turn_replies))
            """)); // T-09: the IDs never reached the AI
        return await db.ScalarAsync<Guid>("SELECT id FROM hazem.urf_cases");
    }

    [Fact]
    public async Task The_lawyer_approves_and_the_client_hears_who_must_come()
    {
        var id = await BookedWithDocuments();
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        var open = (await staff.GetFromJsonAsync<JsonArray>("/api/panel/urf", Ct))!.Single()!;
        Assert.Equal(("booked", 2, "confirmed"), ((string?)open["state"], (int)open["files"]!, (string?)open["appointment"]));
        _factory.Files.Objects["office-sensitive/wa/2026/11/id-1.jpg"] = ([0xFF, 0xD8, 0xFF], "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/panel/urf/{id}/files/0", Ct)).StatusCode); // every role opens them, recorded
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_audit WHERE action = 'urf.file_open'"));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/panel/urf/{id}/review", new { decision = "approved" }, Ct)).StatusCode);

        var lawyer = await SignIn("saad", Permissions.Lawyer);
        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PostAsJsonAsync($"/api/panel/urf/{id}/review", new { decision = "approved" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lawyer.PostAsJsonAsync($"/api/panel/urf/{id}/review", new { decision = "rejected" }, Ct)).StatusCode);
        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        Assert.Equal("تم تأكيد الميعاد يوم بكرة الساعة 12:00 م في المكتب ✅. الحضور: الزوج والزوجة والولي، ومعاكم أصول البطاقات.",
            (string?)JsonNode.Parse(_factory.N8n.Requests.Skip(sends).First().Body)!["text"]);
        Assert.Equal("approved|B-11 v1.15|urf.approved", await db.ScalarAsync<string>("""
            SELECT c.state || '|' || c.policy_version || '|' || (SELECT action FROM hazem.panel_audit WHERE action = 'urf.approved') FROM hazem.urf_cases c
            """));
    }

    [Fact]
    public async Task The_lawyer_refuses_the_appointment_is_cancelled_by_the_office_and_paid_back()
    {
        var id = await BookedWithDocuments();
        var manager = await SignIn("boss", Permissions.Manager);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync($"/api/panel/urf/{id}/review", new { decision = "rejected" }, Ct)).StatusCode);
        // The calendar worker did not run in this test, so there is no event to remove: «removed» at once. Nobody checked
        // the receipt yet: no refund is promised until a person finds the money.
        Assert.Equal("lawyer_rejected|cancelled_by_office|removed|proof_received|0", await db.ScalarAsync<string>("""
            SELECT c.state || '|' || a.state || '|' || a.calendar_state || '|' || p.state
                   || '|' || (SELECT count(*) FROM hazem.staff_tasks WHERE type = 'refund')
              FROM hazem.urf_cases c JOIN hazem.appointments a ON a.id = c.appointment_id JOIN hazem.payments p ON p.appointment_id = a.id
            """));
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync($"/api/panel/payments/{await db.ScalarAsync<Guid>("SELECT appointment_id FROM hazem.urf_cases")}/review",
            new { state = "verified", received = 5000 }, Ct)).StatusCode);
        Assert.Equal("refund_requested|5000|office_cancelled|1", await db.ScalarAsync<string>("""
            SELECT p.state || '|' || p.refund_due || '|' || p.refund_reason || '|' || (SELECT count(*) FROM hazem.staff_tasks WHERE type = 'refund')
              FROM hazem.payments p
            """)); // the money came for a refused case: it all goes back
        var sends = _factory.N8n.Requests.Count;
        await Notices();
        Assert.StartsWith("نعتذر لحضرتك، الأستاذ راجع الورق", (string?)JsonNode.Parse(_factory.N8n.Requests.Skip(sends).First().Body)!["text"]);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.appointments WHERE client_notice = 'pending'")); // no B-22 list
    }

    [Fact]
    public async Task No_document_24_hours_before_gives_the_office_a_task_once()
    {
        await UrfOn();
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        await Say("عايز أعمل جواز عرفي");
        await Say("30 و 25");
        await Press(UrfAnswer.Id(UrfStep.WifePast, "first"));
        await Press(UrfAnswer.Id(UrfStep.Guardian, "brother"));
        await Press(UrfAnswer.Id(UrfStep.Witnesses, "own"));
        await Press("bk:s:mw:" + new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds());
        var lawyer = await SignIn("saad", Permissions.Lawyer);
        var id = await db.ScalarAsync<Guid>("SELECT id FROM hazem.urf_cases");
        Assert.Equal(HttpStatusCode.Conflict, (await lawyer.PostAsJsonAsync($"/api/panel/urf/{id}/review", new { decision = "approved" }, Ct)).StatusCode);
        // ↑ only a confirmed appointment: the hold may still run out
        await db.ExecAsync("UPDATE hazem.appointments SET starts_at = now() + interval '20 hours', ends_at = now() + interval '21 hours'");
        await Notices();
        await Notices();
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'urf_documents_missing'"));

        // The hold runs out: the case goes back to picking a time, and no longer takes files.
        await db.ExecAsync("UPDATE hazem.appointments SET state = 'expired'");
        await Notices();
        Assert.Equal("documents_pending|true", await db.ScalarAsync<string>("SELECT state || '|' || (appointment_id IS NULL) FROM hazem.urf_cases"));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.urf_cases c, hazem.file_target(c.person_id, false) t WHERE t.kind = 'urf_documents'"));
    }

    [Fact]
    public async Task Under_18_is_closed_and_the_divorce_with_children_gets_the_consultation()
    {
        await UrfOn();
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        await Say("عايز أعمل جواز عرفي");
        Assert.Equal("نعتذر لحضرتك، المكتب ما بيعملش عقود جواز لحد أقل من 18 سنة", Text(await Say("30 و 17")));
        Assert.Equal("rejected_age|B-11 v1.15", await db.ScalarAsync<string>("SELECT state || '|' || policy_version FROM hazem.urf_cases"));

        await Say("عايز طلاق عرفي");
        await Press(UrfAnswer.Id(UrfStep.HasCopy, "yes"));
        await Press(UrfAnswer.Id(UrfStep.Agreed, "yes"));
        var offer = await Press(UrfAnswer.Id(UrfStep.Children, "yes"));
        Assert.StartsWith("الأستاذ يقدر يشوف الموضوع مع حضرتك في استشارة", Text(offer));
        Assert.Equal("redirected_consultation", await db.ScalarAsync<string>("SELECT state FROM hazem.urf_cases WHERE kind = 'divorce'"));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.appointments"));
    }
}
