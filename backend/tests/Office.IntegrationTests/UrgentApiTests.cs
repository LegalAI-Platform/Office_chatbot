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
/// v1.34 (B-09.6, test only): with urgent.alert_in_window on, the alert goes first as a normal message inside the 24 hours.
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
        UPDATE hazem.settings SET value = 'false' WHERE key IN ('booking.enabled', 'urgent.alert_in_window');
        UPDATE hazem.settings SET value = '' WHERE key IN ('booking.calendar_id', 'office.instapay', 'office.wallet_number', 'office.wallet_provider',
          'office.lawyer_whatsapp', 'office.manager_whatsapp');
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.generated_replies, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
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

    /// <summary>
    /// «السلام عليكم», the urgent row, the name it asks first (v1.35), then the receipt five minutes later: the call is booked now.
    /// </summary>
    private async Task<Guid> Paid()
    {
        await Turn(new JsonObject { ["type"] = "text", ["text"] = "السلام عليكم" });
        Assert.Equal("تمام، ممكن اسم حضرتك عشان أسجّل الميعاد؟", (string)(await Press("sv:urgent"))[^1]!["text"]!);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.appointments")); // nothing held before the name
        var urgent = await Turn(new JsonObject { ["type"] = "text", ["text"] = "محمد أحمد علي حسن" });
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

    private static void IsAlert(JsonNode t, string to, Guid call, string time = "12:05 م")
    {
        Assert.Equal(("send_template", to, "urgent_alert", "ar"), ((string)t["type"]!, (string)t["wa_id"]!, (string)t["template_name"]!, (string)t["language"]!));
        var body = t["components"]![0]!["parameters"]!.AsArray().Select(p => (string)p!["text"]!).ToList();
        Assert.Equal(["محمد أحمد علي حسن", "01000000113", time], body); // v1.35: the name told at the urgent row
        Assert.Equal($"ug:see:{call:N}", (string)t["components"]![1]!["parameters"]![0]!["payload"]!);
    }

    /// <summary>What «شوف التحويل» (or a stale press) sends the presser: the receipt as a document, then the two buttons.</summary>
    private static void IsReceiptFor(List<JsonNode> sent, string to, Guid receipt, string key, string time = "12:05 م")
    {
        Assert.Equal(2, sent.Count);
        Assert.Equal(("send_document", to), ((string)sent[0]["type"]!, (string)sent[0]["wa_id"]!));
        Assert.Contains(key, (string)sent[0]["document"]!["link"]!, StringComparison.Ordinal);
        Assert.Equal($"🚨 استشارة عاجلة مدفوعة: محمد أحمد علي حسن، 01000000113. المكالمة الساعة {time}. اتأكد من التحويل في الصورة.", (string)sent[0]["text"]!);
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
        IsAlert(Assert.Single(SentSince(before)), Lawyer, call, "12:08 م"); // the call set again from receipt 2

        // «تمام» on receipt 1 (the owner's friend's case): refused, nothing to the client, receipt 2 is sent to the presser instead.
        before = _factory.N8n.Requests.Count;
        Assert.Empty(await Press($"ug:ok:{first:N}", Lawyer));
        IsReceiptFor(SentSince(before), Lawyer, second, await db.ScalarAsync<string>($"SELECT key FROM hazem.payment_receipts WHERE id = '{second}'") ?? "",
            "12:08 م");
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
        Assert.Equal((Client, "بنتواصل مع المستشار دلوقتي، وهيكلم حضرتك."), ((string)sent[1]["wa_id"]!, (string)sent[1]["text"]!));
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
    public async Task V134_with_the_test_switch_on_the_alert_inside_the_window_is_a_normal_message_with_the_same_button()
    {
        await db.SetAsync("urgent.alert_in_window", "true");
        var call = await Paid();
        var before = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Worker());
        var alert = Assert.Single(SentSince(before)); // the lawyer wrote in the last 24 hours (n8n sent it): no template
        Assert.Equal(("send_interactive", Lawyer), ((string)alert["type"]!, (string)alert["wa_id"]!));
        Assert.Equal("🚨 استشارة عاجلة مدفوعة: محمد أحمد علي حسن، 01000000113. المكالمة الساعة 12:05 م. اتأكد من التحويل في الصورة.", (string)alert["text"]!);
        var see = Assert.Single(alert["interactive"]!["choices"]!.AsArray())!;
        Assert.Equal(($"ug:see:{call:N}", "شوف التحويل"), ((string)see["id"]!, (string)see["title"]!));

        // Its button is the template's: the receipt, then «التحويل تمام، هكلمه» / «فيه مشكلة».
        before = _factory.N8n.Requests.Count;
        Assert.Empty(await Press($"ug:see:{call:N}", Lawyer));
        var receipt = await LatestReceipt(call);
        IsReceiptFor(SentSince(before), Lawyer, receipt, await db.ScalarAsync<string>($"SELECT key FROM hazem.payment_receipts WHERE id = '{receipt}'") ?? "");
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'urgent_call_failed'"));
    }

    [Fact]
    public async Task V134_outside_the_window_the_switch_falls_back_to_the_template_and_it_is_off_by_default()
    {
        Assert.Equal("false", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'urgent.alert_in_window'"));
        await db.SetAsync("urgent.alert_in_window", "true");
        var call = await Paid();
        _factory.N8n.Responder = r => r.Content!.ReadAsStringAsync().Result.Contains("\"send_interactive\"", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":false,"status":"failed","error_code":"OUTSIDE_24H_WINDOW"}""", Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"outbound_id":"00000000-0000-0000-0000-000000000901","status":"sent"}""", Encoding.UTF8,
                    "application/json"),
            };
        var before = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Worker());
        var sent = SentSince(before);
        Assert.Equal(["send_interactive", "send_template"], sent.Select(s => (string)s["type"]!));
        IsAlert(sent[1], Lawyer, call);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'urgent_call_failed'"));
    }

    [Fact]
    public async Task V136_a_client_without_a_name_is_his_whatsapp_name_marked_for_the_office_and_never_for_him()
    {
        await Turn(new JsonObject { ["type"] = "text", ["text"] = "السلام عليكم" });
        await Press("sv:urgent"); // the name asked (v1.35)
        var held = await Receipt(); // a photo instead of the name: held without it, the payment told
        Assert.DoesNotContain(held, a => ((string?)a!["text"] ?? "").Contains("Ahmed", StringComparison.Ordinal)); // never said to him
        Assert.Equal("Ahmed|Ahmed (اسم الواتساب)", await db.ScalarAsync<string>(
            $"SELECT whatsapp_name || '|' || office_name FROM hazem.persons WHERE wa_id = '{Client}'"));

        _clock.Advance(TimeSpan.FromMinutes(5));
        await Receipt();
        var before = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Worker());
        var alert = Assert.Single(SentSince(before));
        Assert.Equal("Ahmed (اسم الواتساب)", (string)alert["components"]![0]!["parameters"]![0]!["text"]!);

        using var scope = _factory.Services.CreateScope();
        var today = DateOnly.FromDateTime(Wednesday12.Date);
        Assert.Equal("Ahmed (اسم الواتساب)", Assert.Single(await scope.ServiceProvider.GetRequiredService<PanelBooking>()
            .AppointmentsAsync(today, today, Ct)).Name); // the panel's bookings
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CalendarSync>().SyncAsync(Ct));
        Assert.Contains("– Ahmed (اسم الواتساب) – +" + Client, Assert.Single(_factory.Google.Events.Values).Summary); // not «من غير اسم»
    }

    [Fact]
    public async Task V138_the_urgent_task_comes_with_the_payment_and_an_urgent_call_never_gets_the_review_task()
    {
        await Turn(new JsonObject { ["type"] = "text", ["text"] = "السلام عليكم" });
        await Press("sv:urgent");
        await Turn(new JsonObject { ["type"] = "text", ["text"] = "محمد أحمد علي حسن" });
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'urgent_request'")); // pressed, not paid

        _clock.Advance(TimeSpan.FromMinutes(5));
        await Receipt();
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'urgent_request'")); // paid

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BookingNotices>().RunAsync(Ct); // the call starts within the review hour
        }

        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'payment_review_due'"));
    }

    [Fact]
    public async Task V139_abu_emad_replayed_gets_his_situation_the_services_and_never_a_paid_offer_and_the_office_sees_it()
    {
        await Paid(); // «محمد أحمد علي حسن» (the AI reads a man's name), paid at 12:05, the call at 12:05
        Assert.Equal("male", await db.ScalarAsync<string>($"SELECT name_gender FROM hazem.persons WHERE wa_id = '{Client}'"));
        var offersBefore = await db.ScalarAsync<long>(
            "SELECT count(*) FROM hazem.turn_replies WHERE text_key IN ('consultation_offer', 'case_offer', 'urgent_price', 'urgent_pay', 'booking_hold')");

        static string Last(JsonArray a) => (string)a[^1]!["text"]!;
        // v1.45: his situation, then «لو حابب تكتب الموضوع…» once (he has not told his matter), never again after
        var hello = await Turn(new JsonObject { ["type"] = "text", ["text"] = "السلام عليكم" });
        Assert.Equal(["أكيد يا أستاذ محمد 👋 حضرتك عندك استشارة عاجلة مدفوعة، والمستشار هيكلم حضرتك خلال دقايق.",
            "لو حابب تكتب الموضوع باختصار قبل الميعاد، اكتبه هنا ويوصل للمستشار."], hello.Select(a => (string)a!["text"]!).TakeLast(2).ToArray()); // after «وعليكم السلام»
        Assert.StartsWith("أكيد يا أستاذ محمد 👋", Last(await Turn(new JsonObject { ["type"] = "text", ["text"] = "انت فاكرني؟" })));
        var services = await Turn(new JsonObject { ["type"] = "text", ["text"] = "خدمات" });
        Assert.Equal("list", (string)services[^1]!["interactive"]!["kind"]!);
        Assert.Equal("معلش لو مش واضح، تحب تشوف خدمات المكتب، ولا تكلم حد من المكتب؟",
            Last(await Turn(new JsonObject { ["type"] = "text", ["text"] = "اي خدمات المكتب" })));
        Assert.Equal("تحت أمرك، احكيلي الموضوع باختصار.", Last(await Turn(new JsonObject { ["type"] = "text", ["text"] = "عاوز احكي مشكلتي" })));
        Assert.Equal("تمام يا أستاذ محمد، وصلت ✅ وهتبقى قدام المستشار.",
            Last(await Turn(new JsonObject { ["type"] = "text", ["text"] = "جاري بني على أرضي" })));
        Assert.Equal(offersBefore, await db.ScalarAsync<long>(
            "SELECT count(*) FROM hazem.turn_replies WHERE text_key IN ('consultation_offer', 'case_offer', 'urgent_price', 'urgent_pay', 'booking_hold')"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'existing_client_message'")); // his topic

        // 3.4: «محادثات محتاجة مراجعة» flags the repeat and «فاكرني؟», and no paid offer after the payment.
        var (year, month) = (await db.ScalarAsync<int>("SELECT extract(year FROM now() AT TIME ZONE 'Africa/Cairo')::int"),
            await db.ScalarAsync<int>("SELECT extract(month FROM now() AT TIME ZONE 'Africa/Cairo')::int"));
        using var scope = _factory.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<Office.Infrastructure.Panel.PanelTools>().ReportAsync(year, month, Ct);
        var reasons = report.Review.Select(r => r.Reason).ToList();
        Assert.Contains("repeated", reasons);
        Assert.Contains("asked_status", reasons);
        Assert.DoesNotContain("offer_after_payment", reasons);
        Assert.All(report.Review, r => Assert.Equal("محمد أحمد علي حسن", r.Client));

        // The alarm: a paid offer to a client who had paid (seeded: it cannot happen through Hazem any more).
        var turn = await db.ScalarAsync<string>($"SELECT turn_id FROM hazem.turns WHERE wa_id = '{Client}' ORDER BY created_at DESC LIMIT 1");
        await db.ExecAsync($"""
            INSERT INTO hazem.turn_replies (idempotency_key, turn_id, text_key, text_version)
            SELECT '{turn}:seeded', '{turn}', key, version FROM hazem.approved_texts WHERE key = 'consultation_offer' AND active
            """);
        report = await scope.ServiceProvider.GetRequiredService<Office.Infrastructure.Panel.PanelTools>().ReportAsync(year, month, Ct);
        Assert.Contains("offer_after_payment", report.Review.Select(r => r.Reason));
    }

    [Fact]
    public async Task V141_the_writer_sees_the_conversation_and_a_bad_answer_sends_the_approved_text_and_is_recorded()
    {
        await Paid();
        await db.SetAsync("hazem.conversational", "true");
        try
        {
            // The tests' fake OpenAI answers a classification, never {"reply"}: the approved text goes, and the table says why.
            var d = await Turn(new JsonObject { ["type"] = "text", ["text"] = "انت فاكرني؟" });
            Assert.StartsWith("أكيد يا أستاذ محمد 👋", (string)d[0]!["text"]!); // v1.45: the topic line may follow it
            Assert.Equal("situation_urgent|invalid_output|", await db.ScalarAsync<string>(
                "SELECT purpose || '|' || guard || '|' || coalesce(text, '') FROM hazem.generated_replies ORDER BY created_at DESC LIMIT 1"));

            // What the writer would see: his words and Hazem's answers, oldest first, numbers masked.
            using (var scope = _factory.Services.CreateScope())
            {
                var lines = await scope.ServiceProvider.GetRequiredService<Office.Application.Writing.IConversationHistory>().RecentAsync(Client, 10, Ct);
                Assert.Equal("السلام عليكم", lines[0].Text);
                Assert.True(lines[0].FromClient);
                Assert.Contains(lines, l => !l.FromClient && l.Text.StartsWith("أكيد يا أستاذ محمد", StringComparison.Ordinal));
                Assert.DoesNotContain(lines, l => l.Text.Contains("01000000000", StringComparison.Ordinal)); // the wallet number masked
            }

            // «انسى الرقم» takes the written replies with the turns.
            await db.ScalarAsync<string>($"SELECT hazem.forget_wa_id('{Client}')::text");
            Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.generated_replies"));
        }
        finally
        {
            await db.SetAsync("hazem.conversational", "false");
        }
    }

    [Fact]
    public async Task V135_a_number_whose_urgent_call_was_alerted_can_be_forgotten()
    {
        await Paid();
        Assert.Equal(1, await Worker()); // the alert: the call now points at its receipt (0035)
        Assert.Equal("1", await db.ScalarAsync<string>($"SELECT hazem.forget_wa_id('{Client}') -> 'deleted' ->> 'appointments'"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.persons WHERE wa_id = '{Client}'"));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.payment_receipts"));
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
        Assert.Equal("201000000555", Office.Application.Booking.UrgentRules.Digits("01000000555"));
        await db.SetAsync("office.lawyer_whatsapp", "201000000555");
        using var scope = _factory.Services.CreateScope();
        var office = await scope.ServiceProvider.GetRequiredService<PanelBooking>().OfficeAsync(Ct);
        Assert.Equal(("201000000555", Manager), (office.LawyerWhatsapp, office.ManagerWhatsapp));
        Assert.Equal("invalid", await scope.ServiceProvider.GetRequiredService<PanelBooking>()
            .SaveOfficeAsync(Guid.NewGuid(), "x", office with { LawyerWhatsapp = "not a number" }, Ct));

        // Saved from «بيانات المكتب» as typed («01…» with spaces): stored in WhatsApp's form; the manager's left empty.
        var by = Guid.NewGuid();
        await db.ExecAsync($"""
            INSERT INTO hazem.panel_users (id, username, display_name, email, role, password_hash, security_stamp)
            VALUES ('{by}', 'urgent.boss', 'Boss', 'boss@example.test', 'manager', '{new string('x', 40)}', '{new string('s', 20)}')
            """);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<PanelBooking>()
            .SaveOfficeAsync(by, "urgent.boss", office with { LawyerWhatsapp = "0100 000 0555", ManagerWhatsapp = " " }, Ct));
        Assert.Equal("201000000555", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'office.lawyer_whatsapp'"));
        Assert.Equal("", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'office.manager_whatsapp'"));
    }
}
