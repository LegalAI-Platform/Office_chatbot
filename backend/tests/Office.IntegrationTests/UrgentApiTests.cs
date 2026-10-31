using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Infrastructure.Booking;

namespace Office.IntegrationTests;

/// <summary>
/// v1.33 (B-09) through the real API and database: the urgent consultation as a paid call-back. The client presses the urgent
/// row, pays, and the worker alerts the lawyer with the template urgent_alert («شوف التحويل»); his press sends him the receipt
/// and «التحويل تمام، هكلمه» / «فيه مشكلة»; the first decision on the latest receipt wins; no decision in time → the manager.
/// The app's clock starts on Wednesday 18 November 2026, 12:00 Cairo (the lawyer's calendar is empty, the hours 10:00–24:00).
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class UrgentApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000113";
    private const string Lawyer = "201000000201";
    private const string Manager = "201000000202";
    private static readonly DateTimeOffset Wednesday12 = new(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly MutableClock _clock = new(Wednesday12.ToUniversalTime());
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.appointment_reminders;
        UPDATE hazem.appointments SET urgent_alert_receipt = NULL;
        DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments; DELETE FROM hazem.appointments;
        DELETE FROM hazem.booking_exceptions; DELETE FROM hazem.booking_changes;
        UPDATE hazem.settings SET value = 'false' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = '' WHERE key IN ('booking.calendar_id', 'office.instapay', 'office.wallet_number', 'office.wallet_provider',
          'office.lawyer_whatsapp', 'office.manager_whatsapp');
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
        DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
        """;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync(Clean);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", Client); // the lawyer and the manager are no test recipients of Hazem's
        await db.ExecAsync($"""
            UPDATE hazem.settings SET value = 'true' WHERE key = 'booking.enabled';
            UPDATE hazem.settings SET value = 'lawyer@example.test' WHERE key = 'booking.calendar_id';
            UPDATE hazem.settings SET value = 'office@instapay' WHERE key = 'office.instapay';
            UPDATE hazem.settings SET value = '01000000000' WHERE key = 'office.wallet_number';
            UPDATE hazem.settings SET value = '{Lawyer}' WHERE key = 'office.lawyer_whatsapp';
            UPDATE hazem.settings SET value = '{Manager}' WHERE key = 'office.manager_whatsapp';
            """);
        _factory = new ApiFactory(db, _clock);
        _n8n = _factory.CreateN8nClient();
    }

    public async ValueTask DisposeAsync()
    {
        _n8n.Dispose();
        await _factory.DisposeAsync();
        await db.ExecAsync(Clean);
        await db.SetAsync("hazem.recipients", "");
    }

    private async Task<JsonArray> Turn(JsonObject message, string waId = Client)
    {
        message["wamid"] = $"wamid.UG-{++_n}";
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0033-{++_n:D12}", ["wa_id"] = waId, ["contact_name"] = waId == Client ? "Ahmed" : "Lawyer",
            ["messages"] = new JsonArray(message),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray();
    }

    private Task<JsonArray> Press(string id, string waId = Client) =>
        Turn(new JsonObject { ["type"] = "interactive", ["interactive"] = new JsonObject { ["kind"] = "button_reply", ["id"] = id, ["title"] = "x" } }, waId);

    /// <summary>The template's quick reply (outside the window it comes as a button with its payload).</summary>
    private Task<JsonArray> QuickReply(string payload, string waId) =>
        Turn(new JsonObject { ["type"] = "button", ["button"] = new JsonObject { ["payload"] = payload, ["text"] = "شوف التحويل" } }, waId);

    private Task<JsonArray> Receipt() => Turn(new JsonObject
    {
        ["type"] = "image",
        ["media"] = new JsonObject
        {
            ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = $"wa/2026/11/receipt-{_n}.jpg", ["mime_type"] = "image/jpeg",
            ["size_bytes"] = 3, ["sha256"] = $"r{_n}",
        },
    });

    /// <summary>«السلام عليكم», the urgent row, then the receipt five minutes later: the call is booked now.</summary>
    private async Task<Guid> Paid()
    {
        await Turn(new JsonObject { ["type"] = "text", ["text"] = "السلام عليكم" });
        var urgent = await Press("sv:urgent");
        Assert.Contains("عشان المستشار يكلم حضرتك في أسرع وقت، حوّل 1,000 على InstaPay: office@instapay", (string)urgent[^1]!["text"]!);
        Assert.Equal("12:00", (await db.ScalarAsync<string>(
            "SELECT to_char(starts_at AT TIME ZONE 'Africa/Cairo', 'HH24:MI') FROM hazem.appointments WHERE state = 'pending_payment'"))!);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var paid = await Receipt();
        Assert.Equal("وصلت صورة التحويل ✅. المستشار هيكلم حضرتك على الرقم ده خلال 10 دقايق.", (string)Assert.Single(paid)!["text"]!);
        return await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments WHERE state = 'confirmed'");
    }

    private async Task<int> Worker()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UrgentNotices>().RunAsync(Ct);
    }

    private List<JsonNode> SentSince(int from) => _factory.N8n.Requests.Skip(from).Select(r => JsonNode.Parse(r.Body)!).ToList();

    private Task<Guid> LatestReceipt(Guid call) => db.ScalarAsync<Guid>($"""
        SELECT r.id FROM hazem.payment_receipts r JOIN hazem.payments p ON p.id = r.payment_id
         WHERE p.appointment_id = '{call}' ORDER BY r.received_at DESC, r.id DESC LIMIT 1
        """);

    private static void IsAlert(JsonNode t, string to, Guid call)
    {
        Assert.Equal(("send_template", to, "urgent_alert", "ar"), ((string)t["type"]!, (string)t["wa_id"]!, (string)t["template_name"]!, (string)t["language"]!));
        var body = t["components"]![0]!["parameters"]!.AsArray().Select(p => (string)p!["text"]!).ToList();
        Assert.Equal(["Ahmed", "01000000113", "12:05 م"], body); // no stored name: the WhatsApp contact name
        Assert.Equal($"ug:see:{call:N}", (string)t["components"]![1]!["parameters"]![0]!["payload"]!);
    }

    /// <summary>What «شوف التحويل» (or a stale press) sends the presser: the receipt as a document, then the two buttons.</summary>
    private static void IsReceiptFor(List<JsonNode> sent, string to, Guid receipt, string key)
    {
        Assert.Equal(2, sent.Count);
        Assert.Equal(("send_document", to), ((string)sent[0]["type"]!, (string)sent[0]["wa_id"]!));
        Assert.Contains(key, (string)sent[0]["document"]!["link"]!, StringComparison.Ordinal);
        Assert.Equal("🚨 استشارة عاجلة مدفوعة: Ahmed، 01000000113. المكالمة الساعة 12:05 م. اتأكد من التحويل في الصورة.", (string)sent[0]["text"]!);
        Assert.Equal(("send_interactive", to), ((string)sent[1]["type"]!, (string)sent[1]["wa_id"]!));
        Assert.Equal([$"ug:ok:{receipt:N}", $"ug:bad:{receipt:N}"],
            sent[1]["interactive"]!["choices"]!.AsArray().Select(c => (string)c!["id"]!));
        Assert.Equal(["التحويل تمام، هكلمه", "فيه مشكلة"], sent[1]["interactive"]!["choices"]!.AsArray().Select(c => (string)c!["title"]!));
    }

    [Fact]
    public async Task The_lawyer_is_alerted_once_with_the_template_and_his_press_shows_the_receipt_then_ok_tells_the_client()
    {
        Assert.Equal(0, await Worker());
        var call = await Paid();
        var before = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Worker()); // the call is within 10 minutes: at once
        IsAlert(Assert.Single(SentSince(before)), Lawyer, call);
        Assert.Equal(0, await Worker()); // once

        var receipt = await LatestReceipt(call);
        before = _factory.N8n.Requests.Count;
        Assert.Empty(await QuickReply($"ug:see:{call:N}", Lawyer)); // read by code; no reply inside the turn
        IsReceiptFor(SentSince(before), Lawyer, receipt, await db.ScalarAsync<string>($"SELECT key FROM hazem.payment_receipts WHERE id = '{receipt}'") ?? "");

        before = _factory.N8n.Requests.Count;
        Assert.Empty(await Press($"ug:ok:{receipt:N}", Lawyer));
        var seen = Assert.Single(SentSince(before));
        Assert.Equal((Client, "المستشار شاف طلب حضرتك، وهيكلمك دلوقتي."), ((string)seen["wa_id"]!, (string)seen["text"]!));
        Assert.Equal("ok", await db.ScalarAsync<string>($"SELECT urgent_decision FROM hazem.payment_receipts WHERE id = '{receipt}'"));
        Assert.True(await db.ScalarAsync<bool>($"SELECT urgent_acked_at IS NOT NULL FROM hazem.appointments WHERE id = '{call}'"));

        // Pressed twice: nothing to anyone (no newer receipt waiting), the decision stays.
        before = _factory.N8n.Requests.Count;
        Assert.Empty(await Press($"ug:ok:{receipt:N}", Lawyer));
        Assert.Empty(await Press($"ug:bad:{receipt:N}", Lawyer)); // «فيه مشكلة» after «تمام» on the same receipt
        Assert.Empty(SentSince(before));
        Assert.Equal("ok", await db.ScalarAsync<string>($"SELECT urgent_decision FROM hazem.payment_receipts WHERE id = '{receipt}'"));

        // No escalation after a decision.
        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0, await Worker());
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type LIKE 'urgent_call%'"));
        // The numbers never reach the logs.
        Assert.DoesNotContain(_factory.Logs.Lines, l => l.Contains(Lawyer, StringComparison.Ordinal) || l.Contains("01000000113", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bad_then_a_new_receipt_alerts_again_and_a_stale_ok_on_the_old_receipt_changes_nothing()
    {
        // The lawyer's number on the lawyer-direct list: his presses are still the urgent desk's, never «رسالة للمستشار».
        await db.ExecAsync($"INSERT INTO hazem.list_entries (list_kind, wa_id, import_batch) VALUES ('lawyer_direct', '{Lawyer}', gen_random_uuid())");
        var call = await Paid();
        await Worker();
        var first = await LatestReceipt(call);

        // «فيه مشكلة» on receipt 1: B-05's text to the client, the office checks the transfer.
        var before = _factory.N8n.Requests.Count;
        Assert.Empty(await Press($"ug:bad:{first:N}", Lawyer));
        var told = Assert.Single(SentSince(before));
        Assert.Equal(Client, (string)told["wa_id"]!);
        Assert.StartsWith("⚠️ تحويل ميعاد النهارده الساعة 12:05 م ما وصلش لحد دلوقتي.", (string)told["text"]!, StringComparison.Ordinal);
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'payment_review_due'"));
        Assert.Equal("not_received", await db.ScalarAsync<string>($"SELECT state FROM hazem.payments WHERE appointment_id = '{call}'"));

        // The client sends receipt 2: attached as usual, and the lawyer is alerted again at once.
        var startedAt = await db.ScalarAsync<string>($"SELECT starts_at::text FROM hazem.appointments WHERE id = '{call}'");
        _clock.Advance(TimeSpan.FromMinutes(3));
        await Receipt();
        var second = await LatestReceipt(call);
        Assert.NotEqual(first, second);
        // the call is set again from the new receipt (never left at a time that may have passed)
        Assert.NotEqual(startedAt, await db.ScalarAsync<string>($"SELECT starts_at::text FROM hazem.appointments WHERE id = '{call}'"));
        before = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Worker());
        IsAlert(Assert.Single(SentSince(before)), Lawyer, call);

        // «تمام» on receipt 1 (the owner's friend's case): refused, nothing to the client, receipt 2 is sent to the presser instead.
        before = _factory.N8n.Requests.Count;
        Assert.Empty(await Press($"ug:ok:{first:N}", Lawyer));
        IsReceiptFor(SentSince(before), Lawyer, second, await db.ScalarAsync<string>($"SELECT key FROM hazem.payment_receipts WHERE id = '{second}'") ?? "");
        Assert.Equal("bad", await db.ScalarAsync<string>($"SELECT urgent_decision FROM hazem.payment_receipts WHERE id = '{first}'"));
        Assert.Null(await db.ScalarAsync<string>($"SELECT urgent_decision FROM hazem.payment_receipts WHERE id = '{second}'"));
        Assert.False(await db.ScalarAsync<bool>($"SELECT urgent_acked_at IS NOT NULL FROM hazem.appointments WHERE id = '{call}'"));

        // Then «تمام» on receipt 2: urgent_seen.
        before = _factory.N8n.Requests.Count;
        await Press($"ug:ok:{second:N}", Lawyer);
        Assert.Equal("المستشار شاف طلب حضرتك، وهيكلمك دلوقتي.", (string)Assert.Single(SentSince(before))["text"]!);
    }

    [Fact]
    public async Task No_decision_in_time_goes_to_the_manager_the_client_hears_urgent_waiting_once_and_the_managers_get_an_urgent_task()
    {
        var call = await Paid();
        await Worker();
        _clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(0, await Worker()); // not yet

        _clock.Advance(TimeSpan.FromMinutes(2));
        var before = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Worker());
        var sent = SentSince(before);
        Assert.Equal(2, sent.Count);
        IsAlert(sent[0], Manager, call);
        Assert.Equal((Client, "بنوصّل للمستشار، ثواني وهيكلم حضرتك."), ((string)sent[1]["wa_id"]!, (string)sent[1]["text"]!));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'urgent_call_unanswered'"));
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<Office.Application.Panel.IPanelAlertStore>().MakeAsync(Ct);
        }

        Assert.Equal("task_urgent", await db.ScalarAsync<string>(
            "SELECT a.kind FROM hazem.panel_alerts a JOIN hazem.staff_tasks t ON t.id = a.task_id WHERE t.type = 'urgent_call_unanswered'"));

        _clock.Advance(TimeSpan.FromMinutes(20));
        before = _factory.N8n.Requests.Count;
        Assert.Equal(0, await Worker()); // once
        Assert.Empty(SentSince(before));

        // The manager's press works the same way.
        before = _factory.N8n.Requests.Count;
        await QuickReply($"ug:see:{call:N}", Manager);
        Assert.All(SentSince(before), s => Assert.Equal(Manager, (string)s["wa_id"]!));
    }

    [Fact]
    public async Task Without_the_lawyer_s_number_or_with_the_template_refused_the_office_gets_an_urgent_task_once()
    {
        await db.SetAsync("office.lawyer_whatsapp", "");
        await Paid();
        var before = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Worker());
        Assert.Empty(SentSince(before)); // nothing to send to
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'urgent_call_failed'"));
        Assert.Equal(0, await Worker()); // never a retry in a loop
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'urgent_call_failed'"));
    }

    [Fact]
    public async Task The_template_refused_by_meta_is_an_urgent_task()
    {
        await Paid();
        _factory.N8n.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject { ["ok"] = false, ["status"] = "failed", ["error_code"] = "TEMPLATE_NOT_APPROVED" }.ToJsonString(),
                Encoding.UTF8, "application/json"),
        };
        Assert.Equal(1, await Worker());
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'urgent_call_failed'"));
    }

    [Fact]
    public async Task The_office_s_ok_on_an_urgent_transfer_sends_no_confirmation_and_the_review_stays_the_panel_s()
    {
        var call = await Paid();
        Assert.Equal("proof_received", await db.ScalarAsync<string>($"SELECT state FROM hazem.payments WHERE appointment_id = '{call}'"));
        Assert.Equal("marked", await db.ScalarAsync<string>($"SELECT hazem.mark_payment('{call}', 'verified', 1000, NULL)->>'status'"));
        Assert.Null(await db.ScalarAsync<string>($"SELECT client_notice FROM hazem.payments WHERE appointment_id = '{call}'"));

        // «ما وصلش» still tells the client, as before.
        Assert.Equal("marked", await db.ScalarAsync<string>($"SELECT hazem.mark_payment('{call}', 'not_received', NULL, NULL)->>'status'"));
        Assert.Equal("payment_not_received", await db.ScalarAsync<string>($"SELECT notice_kind FROM hazem.payments WHERE appointment_id = '{call}'"));
    }

    [Fact]
    public async Task The_panel_saves_the_two_numbers_as_whatsapp_writes_them()
    {
        Assert.Equal("201102938589", Office.Application.Booking.UrgentRules.Digits("01102938589"));
        await db.SetAsync("office.lawyer_whatsapp", "201102938589");
        using var scope = _factory.Services.CreateScope();
        var office = await scope.ServiceProvider.GetRequiredService<PanelBooking>().OfficeAsync(Ct);
        Assert.Equal(("201102938589", Manager), (office.LawyerWhatsapp, office.ManagerWhatsapp));
        Assert.Equal("invalid", await scope.ServiceProvider.GetRequiredService<PanelBooking>()
            .SaveOfficeAsync(Guid.NewGuid(), "x", office with { LawyerWhatsapp = "not a number" }, Ct));

        // Saved from «بيانات المكتب» as typed («01…» with spaces): stored in WhatsApp's form; the manager's left empty.
        var by = Guid.NewGuid();
        await db.ExecAsync($"""
            INSERT INTO hazem.panel_users (id, username, display_name, email, role, password_hash, security_stamp)
            VALUES ('{by}', 'urgent.boss', 'Boss', 'boss@example.test', 'manager', '{new string('x', 40)}', '{new string('s', 20)}')
            """);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<PanelBooking>()
            .SaveOfficeAsync(by, "urgent.boss", office with { LawyerWhatsapp = "0110 293 8589", ManagerWhatsapp = " " }, Ct));
        Assert.Equal("201102938589", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'office.lawyer_whatsapp'"));
        Assert.Equal("", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'office.manager_whatsapp'"));
    }
}
