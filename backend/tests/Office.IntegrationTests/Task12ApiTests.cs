using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Application.Panel;
using Office.Application.Reminders;
using Office.Infrastructure.Booking;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 12 (DECISIONS v1.16, B-18 / M-03) through the real API and database: the reminders the worker makes and sends
/// (the message inside the 24-hour window, the template outside it, a task when neither can go), how they follow a moved
/// or cancelled appointment, «هحضر ✅» and «تغيير أو إلغاء», and what «المواعيد» shows. The app's clock starts on
/// Wednesday 18 November 2026, 10:00 Cairo, and the client books Friday 20 November at 12:00.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class Task12ApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000112";
    private const string Password = "a long test password 1";
    private static readonly DateTimeOffset Wednesday10 = new(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Friday12 = new(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly MutableClock _clock = new(Wednesday10.ToUniversalTime());
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.appointment_reminders;
        DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments; DELETE FROM hazem.appointments;
        DELETE FROM hazem.booking_exceptions; DELETE FROM hazem.booking_changes;
        UPDATE hazem.settings SET value = 'false' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = '' WHERE key IN ('booking.calendar_id', 'office.instapay', 'office.wallet_number', 'office.wallet_provider',
          'office.address', 'office.map_url');
        -- 1.32: the «المستشار» version of a B-18 text (reminder_details_phone…) is 'DECISIONS v1.32 B-27'
        UPDATE hazem.approved_texts SET active = true WHERE source IN ('proposal TASK 08', 'proposal TASK 09', 'proposal TASK 12', 'DECISIONS v1.16 B-18', 'DECISIONS v1.25 B-18', 'DECISIONS v1.32 B-27') AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.generated_replies, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
        DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
        """;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync(Clean);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", Client);
        await db.ExecAsync("""
            UPDATE hazem.settings SET value = 'true' WHERE key = 'booking.enabled';
            UPDATE hazem.settings SET value = 'lawyer@example.test' WHERE key = 'booking.calendar_id';
            UPDATE hazem.settings SET value = 'office@instapay' WHERE key = 'office.instapay';
            UPDATE hazem.settings SET value = '01000000000' WHERE key = 'office.wallet_number';
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

    private async Task<JsonArray> Turn(JsonObject message)
    {
        message["wamid"] = $"wamid.T12-{++_n}";
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0012-{++_n:D12}", ["wa_id"] = Client, ["messages"] = new JsonArray(message),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray();
    }

    private Task<JsonArray> Say(string text) => Turn(new JsonObject { ["type"] = "text", ["text"] = text });

    /// <summary>A reminder's button: inside the window an interactive reply, outside it the template's quick reply (button.payload).</summary>
    private Task<JsonArray> Press(string id) =>
        Turn(new JsonObject { ["type"] = "interactive", ["interactive"] = new JsonObject { ["kind"] = "button_reply", ["id"] = id, ["title"] = "x" } });

    private Task<JsonArray> Receipt() => Turn(new JsonObject
    {
        ["type"] = "image",
        ["media"] = new JsonObject
        {
            ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = $"wa/2026/11/receipt-{_n}.jpg", ["mime_type"] = "image/jpeg",
            ["size_bytes"] = 3, ["sha256"] = $"r{_n}",
        },
    });

    /// <summary>The client books Friday 12:00 (<paramref name="service"/>) and sends the receipt: confirmed.</summary>
    private async Task<Guid> Confirmed(string service = "pv")
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        await Press($"bk:s:{service}:{Friday12.ToUnixTimeSeconds()}");
        await Receipt();
        return await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments WHERE state = 'confirmed'");
    }

    private async Task<int> Reminders()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderNotices>().RunAsync(Ct);
    }

    private Task<string?> Rows(Guid id) => db.ScalarAsync<string>($"""
        SELECT string_agg(kind || ':' || state || ':' || coalesce(channel, skip_reason, error_code, '-'), ',' ORDER BY id)
          FROM hazem.appointment_reminders WHERE appointment_id = '{id}'
        """);

    private void At(DateTimeOffset when) => _clock.Advance(when.ToUniversalTime() - _clock.GetUtcNow());

    private List<JsonNode> SentSince(int from) => _factory.N8n.Requests.Skip(from).Select(r => JsonNode.Parse(r.Body)!).ToList();

    private static HttpResponseMessage Answer(bool ok, string? code = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new JsonObject
        {
            ["ok"] = ok, ["status"] = ok ? "sent" : "failed", ["error_code"] = code, ["wamid"] = ok ? $"wamid.R-{Guid.NewGuid():N}" : null,
        }.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private static string TypeOf(HttpRequestMessage r) =>
        (string)JsonNode.Parse(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!["type"]!;

    // ---------- the texts ----------

    [Fact]
    public async Task Migration_0017_added_the_reminder_texts_active_within_their_limits()
    {
        // 1.25: reminder_details_phone v2 (where the lawyer calls) replaced v1; 1.32: three of them got their «المستشار» version
        Assert.Equal("9|13", await db.ScalarAsync<string>(
            """
            SELECT count(*) FILTER (WHERE active) || '|' || count(*) FROM hazem.approved_texts
             WHERE key IN ('reminder_day_before', 'reminder_2h_before', 'btn_reminder_attend', 'btn_reminder_change', 'reminder_attend_ok',
                           'reminder_details_office', 'reminder_details_phone', 'reminder_details_urf', 'reminder_old_button')
            """));
        Assert.Equal(0L, await db.ScalarAsync<long>("""
            SELECT count(*) FROM hazem.approved_texts
             WHERE (source = 'proposal TASK 12' OR key LIKE 'reminder_%') AND char_length(text) > max_chars
            """));
        Assert.Equal(ReminderTexts.Keys.Count, (int)await db.ScalarAsync<long>(
            $"SELECT count(*) FROM hazem.approved_texts WHERE active AND key IN ({string.Join(", ", ReminderTexts.Keys.Select(k => $"'{k}'"))})"));
    }

    // ---------- the worker ----------

    [Fact]
    public async Task A_confirmed_booking_gets_both_reminders_and_each_goes_out_once()
    {
        var id = await Confirmed();
        Assert.Equal(0, await Reminders());
        Assert.Equal("day_before:scheduled:-,two_hours:scheduled:-", await Rows(id));
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2)), await db.ScalarAsync<DateTime>(
            $"SELECT due_at FROM hazem.appointment_reminders WHERE appointment_id = '{id}' AND kind = 'day_before'"));

        At(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2)));
        var from = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Reminders());
        var day = Assert.Single(SentSince(from));
        Assert.Equal("send_interactive", (string)day["type"]!);
        // Its own key: the appointment, the kind and the time (a rollback reuses row ids, never these).
        Assert.Equal($"hazem:reminder:{id:N}:day_before:{Friday12.ToUnixTimeSeconds()}:0", (string)day["idempotency_key"]!);
        var text = (string)day["text"]!;
        Assert.Contains("بكرة الجمعة 20/11 الساعة 12:00 م", text);
        Assert.Contains("نوع الاستشارة: استشارة بالتليفون أو الفيديو", text);
        Assert.Contains("المستشار هيكلم حضرتك على واتساب على نفس الرقم ده في الميعاد", text);
        Assert.DoesNotContain("{{", text);
        var buttons = day["interactive"]!["choices"]!.AsArray();
        Assert.Equal([ReminderButtons.Attend(id, Friday12), ReminderButtons.Change(id)], buttons.Select(b => (string)b!["id"]!));
        Assert.Equal(["هحضر ✅", "تغيير أو إلغاء"], buttons.Select(b => (string)b!["title"]!));
        Assert.Equal(0, await Reminders()); // once
        Assert.Equal("day_before:sent:message,two_hours:scheduled:-", await Rows(id));

        At(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(2)));
        from = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Reminders());
        var two = Assert.Single(SentSince(from));
        Assert.Equal("send_text", (string)two["type"]!);
        Assert.Contains("النهارده الساعة 12:00 م", (string)two["text"]!);
        Assert.Equal("day_before:sent:message,two_hours:sent:message", await Rows(id));
    }

    [Fact]
    public async Task Outside_the_24_hour_window_the_template_carries_the_same_words_and_buttons()
    {
        var id = await Confirmed();
        await Reminders();
        _factory.N8n.Responder = r => TypeOf(r) == "send_template" ? Answer(true) : Answer(false, ReminderNotices.OutsideWindow);
        At(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2)));
        var from = _factory.N8n.Requests.Count;
        await Reminders();

        var sent = SentSince(from);
        Assert.Equal(["send_interactive", "send_template"], sent.Select(s => (string)s["type"]!));
        var t = sent[1];
        Assert.Equal(("reminder_day_before", "ar"), ((string)t["template_name"]!, (string)t["language"]!));
        var parts = t["components"]!.AsArray();
        Assert.Equal(["الجمعة 20/11", "12:00 م", "استشارة بالتليفون أو الفيديو", "المستشار هيكلم حضرتك على واتساب على نفس الرقم ده في الميعاد"],
            parts[0]!["parameters"]!.AsArray().Select(p => (string)p!["text"]!));
        Assert.Equal([ReminderButtons.Attend(id, Friday12), ReminderButtons.Change(id)],
            parts.Skip(1).Select(p => (string)p!["parameters"]![0]!["payload"]!));
        Assert.Equal("day_before:sent:template,two_hours:scheduled:-", await Rows(id));

        // The template's quick reply comes back as button.payload: «هحضر ✅» works the same.
        var actions = await Turn(new JsonObject { ["type"] = "button", ["button"] = new JsonObject { ["text"] = "هحضر ✅", ["payload"] = ReminderButtons.Attend(id, Friday12) } });
        Assert.Contains("في انتظار حضرتك", (string)actions[0]!["text"]!);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.appointments WHERE id = '{id}' AND attendance_for = starts_at"));
    }

    [Fact]
    public async Task A_reminder_that_cannot_go_out_is_a_task_for_the_office()
    {
        var id = await Confirmed();
        await Reminders();
        _factory.N8n.Responder = r => Answer(false, TypeOf(r) == "send_template" ? "TEMPLATE_NOT_APPROVED" : ReminderNotices.OutsideWindow);
        At(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2)));
        await Reminders();

        Assert.Equal("day_before:failed:TEMPLATE_NOT_APPROVED,two_hours:scheduled:-", await Rows(id));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'reminder_failed'"));
        var from = _factory.N8n.Requests.Count;
        await Reminders();
        Assert.Equal(from, _factory.N8n.Requests.Count); // never retried, never a second task
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'reminder_failed'"));
    }

    [Fact]
    public async Task An_office_reminder_names_the_address_and_the_map_and_never_goes_without_them()
    {
        var id = await Confirmed("of");
        await Reminders();
        At(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2)));
        var from = _factory.N8n.Requests.Count;
        await Reminders();
        Assert.Equal(from, _factory.N8n.Requests.Count); // P1: the address is not written yet
        Assert.Equal("day_before:failed:details_missing,two_hours:scheduled:-", await Rows(id));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'reminder_failed'"));

        await db.SetAsync("office.address", "12 شارع التحرير، الدقي");
        await db.SetAsync("office.map_url", "https://maps.example/office");
        At(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(2)));
        await Reminders();
        var text = (string)SentSince(from).Single()["text"]!;
        Assert.Contains("المكتب في 12 شارع التحرير، الدقي، واللوكيشن: https://maps.example/office", text);
        Assert.Contains("نوع الاستشارة: مقابلة في المكتب", text);
    }

    [Fact]
    public async Task Reminders_follow_a_moved_or_cancelled_booking_and_a_hold_gets_none()
    {
        var id = await Confirmed();
        await Reminders();
        await db.ExecAsync($"UPDATE hazem.appointments SET starts_at = starts_at + interval '1 hour', ends_at = ends_at + interval '1 hour' WHERE id = '{id}'");
        await Reminders();
        Assert.Equal("day_before:cancelled:-,two_hours:cancelled:-,day_before:scheduled:-,two_hours:scheduled:-", await Rows(id));

        await db.ExecAsync($"UPDATE hazem.appointments SET state = 'cancelled_by_client' WHERE id = '{id}'");
        await Reminders();
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.appointment_reminders WHERE state = 'scheduled'"));

        // A hold (pending_payment) waits for its receipt: no reminder before it is confirmed (1.13).
        await Press($"bk:s:pv:{Friday12.AddHours(3).ToUnixTimeSeconds()}");
        var hold = await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments WHERE state = 'pending_payment'");
        Assert.NotEqual(Guid.Empty, hold);
        await Reminders();
        Assert.Null(await Rows(hold));
    }

    [Fact]
    public async Task A_send_cut_off_half_way_is_tried_again_under_the_same_key()
    {
        var id = await Confirmed();
        await Reminders();
        At(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2)));
        // The API stopped between claiming it and recording the send.
        await db.ExecAsync($"UPDATE hazem.appointment_reminders SET state = 'sending' WHERE appointment_id = '{id}' AND kind = 'day_before'");
        var from = _factory.N8n.Requests.Count;
        await Reminders();
        Assert.Equal(from, _factory.N8n.Requests.Count); // claimed by a round that may still be sending: left alone

        // Six minutes later (the trigger would set updated_at to now(), so it is off for this one change).
        await db.ExecAsync($"""
            ALTER TABLE hazem.appointment_reminders DISABLE TRIGGER trg_appointment_reminders_updated_at;
            UPDATE hazem.appointment_reminders SET updated_at = now() - interval '6 minutes' WHERE appointment_id = '{id}' AND kind = 'day_before';
            ALTER TABLE hazem.appointment_reminders ENABLE TRIGGER trg_appointment_reminders_updated_at;
            """);
        await Reminders();
        var again = Assert.Single(SentSince(from));
        Assert.Equal($"hazem:reminder:{id:N}:day_before:{Friday12.ToUnixTimeSeconds()}:0", (string)again["idempotency_key"]!); // n8n: «already sent» if it went
        Assert.Equal("day_before:sent:message,two_hours:scheduled:-", await Rows(id));
    }

    [Fact]
    public async Task Without_every_reminder_text_none_is_sent()
    {
        var id = await Confirmed();
        await Reminders();
        await db.ExecAsync("UPDATE hazem.approved_texts SET active = false WHERE key = 'reminder_details_phone'");
        At(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2)));
        var from = _factory.N8n.Requests.Count;
        await Reminders();
        Assert.Equal(from, _factory.N8n.Requests.Count);
        Assert.Equal("day_before:skipped:off,two_hours:scheduled:-", await Rows(id));
    }

    [Fact]
    public async Task Confirmed_after_the_day_before_time_only_the_two_hours_one_is_made()
    {
        At(new DateTimeOffset(2026, 11, 19, 13, 0, 0, TimeSpan.FromHours(2))); // 23 hours before
        var id = await Confirmed();
        await Reminders();
        Assert.Equal("day_before:skipped:passed,two_hours:scheduled:-", await Rows(id));
    }

    // ---------- the buttons and the panel ----------

    [Fact]
    public async Task I_will_come_is_recorded_shown_in_the_panel_and_sent_to_the_calendar()
    {
        var id = await Confirmed();
        await Reminders();
        await db.ExecAsync($"UPDATE hazem.appointments SET calendar_event_id = 'ev1', calendar_state = 'synced' WHERE id = '{id}'");
        At(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2)));
        await Reminders();

        var reply = await Press(ReminderButtons.Attend(id, Friday12));
        Assert.Equal("تمام ✅ في انتظار حضرتك بكرة الساعة 12:00 م.", (string)Assert.Single(reply)!["text"]!);
        Assert.Equal("update", await db.ScalarAsync<string>($"SELECT calendar_state FROM hazem.appointments WHERE id = '{id}'"));

        var staff = await SignIn("saad", Permissions.Lawyer);
        var list = (await staff.GetFromJsonAsync<JsonArray>("/api/panel/booking/appointments?from=2026-11-20&to=2026-11-20", Ct))!;
        var a = Assert.Single(list)!;
        Assert.True((bool)a["attending"]!);
        var r = a["reminders"]!.AsArray();
        Assert.Equal(["day_before", "two_hours"], r.Select(x => (string)x!["kind"]!));
        Assert.Equal(("sent", "message", "scheduled"), ((string)r[0]!["state"]!, (string)r[0]!["channel"]!, (string)r[1]!["state"]!));

        // «تغيير أو إلغاء»: the existing step, with B-15 and B-22 as they are.
        var change = await Press(ReminderButtons.Change(id));
        Assert.Equal("send_interactive", (string)change[0]!["type"]!);
        Assert.Contains("تحب تغيّر الميعاد ولا تلغيه؟", (string)change[0]!["text"]!);
    }

    private async Task<HttpClient> SignIn(string name, string role)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PanelAccounts>().CreateAsync(null,
                new PanelAccounts.NewAccount(name, name, $"{name}@example.test", role, null), Password, Ct);
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
}
