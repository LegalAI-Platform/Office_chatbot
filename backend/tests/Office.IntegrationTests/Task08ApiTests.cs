using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Office.Application.Panel;
using Office.Infrastructure.Booking;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 08 (DECISIONS v1.12) through the real API and database: booking from the conversation, the
/// panel's hours and exceptions, never two appointments at once, the calendar, and RBAC.
/// The clock stands at Thursday 19 November 2026, 12:00 Cairo.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class Task08ApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000091";
    private const string Password = "a long test password 1";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2));
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments; DELETE FROM hazem.appointments; DELETE FROM hazem.booking_exceptions; DELETE FROM hazem.booking_changes;
        DELETE FROM hazem.booking_weekly_hours;
        INSERT INTO hazem.booking_weekly_hours (weekday, start_minute, end_minute) SELECT d, 600, 1440 FROM generate_series(0, 6) d;
        UPDATE hazem.settings SET value = CASE key
          WHEN 'booking.enabled' THEN 'false' WHEN 'booking.buffer_minutes' THEN '10' WHEN 'booking.min_notice_hours' THEN '2'
          WHEN 'booking.horizon_days' THEN '14' WHEN 'booking.slot_step_minutes' THEN '15' WHEN 'booking.offer_count' THEN '4'
          WHEN 'booking.hold_minutes' THEN '120' WHEN 'booking.hold_short_minutes' THEN '60'
          WHEN 'booking.hold_short_within_hours' THEN '6' WHEN 'booking.hold_gap_minutes' THEN '15'
          ELSE '' END
         WHERE key LIKE 'booking.%' OR key LIKE 'office.%';
        UPDATE hazem.settings SET value = 'false' WHERE key = 'booking.conflict_auto_message';
        UPDATE hazem.approved_texts SET active = true WHERE source IN ('proposal TASK 08', 'proposal TASK 09') AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
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

    /// <summary>
    /// Booking on: TASK 08's and TASK 09's texts (approved since 0013 and 0014; the booking needs them all),
    /// the office's details and the calendar id.
    /// </summary>
    private Task BookingOn() => db.ExecAsync("""
        UPDATE hazem.approved_texts SET active = true WHERE source = 'proposal TASK 09' AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
        UPDATE hazem.settings SET value = 'true' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = 'lawyer@example.test' WHERE key = 'booking.calendar_id';
        UPDATE hazem.settings SET value = 'office@instapay' WHERE key = 'office.instapay';
        UPDATE hazem.settings SET value = '01000000000' WHERE key = 'office.wallet_number';
        """);

    private async Task<JsonArray> Turn(JsonObject message)
    {
        message["wamid"] = $"wamid.T08-{++_n}";
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0008-{++_n:D12}", ["wa_id"] = Client, ["messages"] = new JsonArray(message),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray();
    }

    private Task<JsonArray> Say(string text) => Turn(new JsonObject { ["type"] = "text", ["text"] = text });

    private Task<JsonArray> Press(string id) =>
        Turn(new JsonObject { ["type"] = "interactive", ["interactive"] = new JsonObject { ["kind"] = "list_reply", ["id"] = id, ["title"] = "x" } });

    /// <summary>TASK 09: a receipt image n8n stored (its place and hash only).</summary>
    private Task<JsonArray> Receipt(string sha = "r1") => Turn(new JsonObject
    {
        ["type"] = "image",
        ["media"] = new JsonObject
        {
            ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = $"wa/2026/11/receipt-{_n}.jpg", ["mime_type"] = "image/jpeg",
            ["size_bytes"] = 1234, ["sha256"] = sha,
        },
    });

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

    /// <summary>A new client through the name, up to the offer's buttons.</summary>
    private async Task<JsonArray> UpToTheOffer()
    {
        await Say("السلام عليكم");
        return await Say("محمد أحمد علي حسن");
    }

    /// <summary>One round of the worker that tells the clients of appointments the office cancelled.</summary>
    private async Task<int> Notices()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BookingNotices>().RunAsync(Ct);
    }

    // ---------- the conversation ----------

    [Fact]
    public async Task A_client_books_from_the_buttons_and_the_event_reaches_the_calendar_without_their_words()
    {
        await BookingOn();
        var offer = await UpToTheOffer();
        var menu = offer.Single(a => (string?)a!["type"] == "send_interactive")!; // v1.30: the services list after the name
        Assert.Equal("list", (string?)menu["interactive"]!["kind"]);
        Assert.Equal(("bk:m:pv", "ربع ساعة · 500 جنيه"), ((string?)menu["interactive"]!["choices"]![1]!["id"], (string?)menu["interactive"]!["choices"]![1]!["description"]));

        Assert.Equal("buttons", (string?)(await Press("bk:m:pv")).Single()!["interactive"]!["kind"]); // 1.25: where the lawyer calls
        var times = (await Press("bk:v:wa")).Single()!;
        Assert.Equal("list", (string?)times["interactive"]!["kind"]);
        var first = times["interactive"]!["choices"]![0]!;
        Assert.Equal("النهارده 2:00 م", (string?)first["title"]); // 12:00 + the 2 hours' notice

        var held = (await Press((string)first["id"]!)).Single()!;
        Assert.Contains("office@instapay", (string?)held["text"]);
        Assert.Contains("قبل الساعة 1:00 م النهارده", (string?)held["text"]); // v1.13: held until the receipt
        Assert.Equal("pending_payment|consultation_phone_video|500|wait", await db.ScalarAsync<string>(
            "SELECT state || '|' || service || '|' || amount || '|' || calendar_state FROM hazem.appointments"));
        using (var scope = _factory.Services.CreateScope())
        {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CalendarSync>().SyncAsync(Ct)); // no event for a hold
        }

        Assert.StartsWith("وصلت صورة التحويل ✅، والميعاد محجوز لحضرتك", (string?)(await Receipt()).Single()!["text"]); // 1.25: until the review
        Assert.Equal("confirmed|create|proof_received", await db.ScalarAsync<string>(
            "SELECT a.state || '|' || a.calendar_state || '|' || p.state FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id"));

        using (var scope = _factory.Services.CreateScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CalendarSync>().SyncAsync(Ct));
        }

        var ev = Assert.Single(_factory.Google.Events.Values);
        Assert.Equal("استشارة بالتليفون أو الفيديو (واتساب) – محمد أحمد علي حسن – +201000000091 – غير معروف – الدفع: مستني مراجعتك", ev.Summary);
        Assert.Equal("synced", await db.ScalarAsync<string>("SELECT calendar_state FROM hazem.appointments"));
    }

    [Fact]
    public async Task Migration_0013_activated_exactly_the_16_booking_texts_approved_by_the_owner() =>
        Assert.Equal("16|16", await db.ScalarAsync<string>("""
            SELECT count(*) FILTER (WHERE active) || '|' || count(*) FROM hazem.approved_texts
             WHERE source = 'proposal TASK 08' AND updated_by = 'owner approval 2026-10-24'
            """));

    [Fact]
    public async Task Without_the_switch_the_offer_is_the_old_text_and_nothing_is_booked()
    {
        var offer = await UpToTheOffer();
        Assert.DoesNotContain(offer.Where(a => (string?)a!["type"] == "send_interactive").SelectMany(a => a!["interactive"]!["choices"]!.AsArray()),
            c => ((string?)c!["id"])!.StartsWith("bk:", StringComparison.Ordinal)); // v1.30: the services list, without the booking's buttons
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.appointments"));
    }

    [Fact]
    public async Task Two_clients_on_the_same_time_only_one_wins()
    {
        await db.ExecAsync("""
            INSERT INTO hazem.persons (id, wa_id) VALUES ('00000000-0000-0000-0000-0000000008a1', '201000000801'),
                                                        ('00000000-0000-0000-0000-0000000008a2', '201000000802');
            """);
        async Task<string> Book(string person, string turn)
        {
            await using var c = new NpgsqlConnection(db.ApiConnectionString);
            await c.OpenAsync(Ct);
            await using var cmd = new NpgsqlCommand(
                $"SELECT hazem.book_appointment(gen_random_uuid(), '{person}', NULL, 'consultation_phone_video', '2026-12-01 14:00+02', '2026-12-01 14:15+02', 500, '{turn}', 10, NULL, '2026-12-01 13:00+02')->>'status'", c);
            return (string)(await cmd.ExecuteScalarAsync(Ct))!;
        }

        // v1.13: the winner holds the time (until the receipt); the hold closes it like a confirmed booking.
        var results = await Task.WhenAll(Book("00000000-0000-0000-0000-0000000008a1", "t1"), Book("00000000-0000-0000-0000-0000000008a2", "t2"));
        Assert.Equal(["held", "taken"], results.Order());
        Assert.Equal("exists", await Book(results[0] == "held" ? "00000000-0000-0000-0000-0000000008a1" : "00000000-0000-0000-0000-0000000008a2",
            results[0] == "held" ? "t1" : "t2")); // the same turn again books nothing new

        // The constraint alone refuses it too, even without the function.
        var e = await Assert.ThrowsAsync<PostgresException>(() => db.ExecAsync("""
            INSERT INTO hazem.appointments (id, person_id, service, starts_at, ends_at, amount, booked_turn_id)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000008a2', 'consultation_office', '2026-12-01 14:05+02', '2026-12-01 14:35+02', 1000, 't3')
            """));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, e.SqlState);
    }

    // ---------- the panel ----------

    [Fact]
    public async Task Closing_a_day_with_a_booking_asks_first_then_tells_the_client_and_undo_reopens_it()
    {
        await BookingOn();
        await UpToTheOffer();
        await Press("bk:s:pv:" + new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds());
        await Receipt(); // paid: the office's cancellation offers the refund (B-22)
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");

        var refused = await staff.PostAsJsonAsync("/api/panel/booking/exceptions", new { day = "2026-11-20", kind = "closed_day", reason = "جلسة" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var listed = (await refused.Content.ReadFromJsonAsync<JsonNode>(Ct))!["appointments"]!.AsArray();
        Assert.Equal("محمد أحمد علي حسن", (string?)Assert.Single(listed)!["name"]);
        Assert.Equal("confirmed", await db.ScalarAsync<string>("SELECT state FROM hazem.appointments"));

        var sends = _factory.N8n.Requests.Count;
        var done = await staff.PostAsJsonAsync("/api/panel/booking/exceptions", new { day = "2026-11-20", kind = "closed_day", confirm = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Equal(1, (int)(await done.Content.ReadFromJsonAsync<JsonNode>(Ct))!["cancelled"]!);
        // Its event was never made (the worker had not run yet): nothing to remove from the calendar.
        Assert.Equal("cancelled_by_office|removed|pending", await db.ScalarAsync<string>(
            "SELECT state || '|' || calendar_state || '|' || client_notice FROM hazem.appointments"));
        Assert.Equal(sends, _factory.N8n.Requests.Count); // the panel's request sends nothing: the worker tells the client
        Assert.Equal(1, await Notices());
        Assert.Equal("sent", await db.ScalarAsync<string>("SELECT client_notice FROM hazem.appointments"));
        var message = JsonNode.Parse(_factory.N8n.Requests.Skip(sends).Single().Body)!;
        Assert.Equal(("send_interactive", "المستشار عنده ظرف في الميعاد ده، تحب ميعاد تاني ولا استرداد كامل؟"), ((string?)message["type"], (string?)message["text"]));
        var rows = message["interactive"]!["choices"]!.AsArray();
        Assert.StartsWith("bk:f:", (string?)rows[^1]!["id"]); // «استرداد كامل»
        Assert.DoesNotContain(rows, r => ((string?)r!["title"])!.StartsWith("بكرة", StringComparison.Ordinal)); // the 20th is closed

        var preview = await staff.GetFromJsonAsync<JsonNode>("/api/panel/booking/preview?day=2026-11-20&service=pv", Ct);
        Assert.Equal(("ok", 0), ((string?)preview!["calendar"], preview["slots"]!.AsArray().Count));

        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync("/api/panel/booking/undo", new { confirm = false }, Ct)).StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.booking_exceptions"));
        preview = await staff.GetFromJsonAsync<JsonNode>("/api/panel/booking/preview?day=2026-11-20&service=pv", Ct);
        Assert.Equal(56, preview!["slots"]!.AsArray().Count); // 10:00–24:00 every 15 minutes, open again
        Assert.Equal("booking.exception_add|booking.undo", await db.ScalarAsync<string>(
            "SELECT string_agg(action, '|' ORDER BY id) FROM hazem.panel_audit WHERE action LIKE 'booking.%'"));
    }

    [Fact]
    public async Task Undo_that_would_strand_a_booking_asks_first_like_any_change()
    {
        await BookingOn();
        var staff = await SignIn("karim", Permissions.Staff, "كريم");
        object[] Week(int end) => Enumerable.Range(0, 7).Select(d => (object)new { weekday = d, ranges = new object[] { new { start = 600, end } } }).ToArray();
        // Mornings only, then the evenings opened too (the change undo would put back).
        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync("/api/panel/booking/week", new { days = Week(720) }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync("/api/panel/booking/week", new { days = Week(1440) }, Ct)).StatusCode);
        await UpToTheOffer();
        await Press("bk:s:pv:" + new DateTimeOffset(2026, 11, 21, 21, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds()); // in the new evening hours
        Assert.Equal("pending_payment", await db.ScalarAsync<string>("SELECT state FROM hazem.appointments")); // a hold counts too

        var asked = await staff.PostAsJsonAsync("/api/panel/booking/undo", new { confirm = false }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, asked.StatusCode); // back to mornings would leave 21:00 outside
        Assert.Single((await asked.Content.ReadFromJsonAsync<JsonNode>(Ct))!["appointments"]!.AsArray());
        Assert.Equal("pending_payment|0", await db.ScalarAsync<string>(
            "SELECT (SELECT state FROM hazem.appointments) || '|' || (SELECT count(*) FROM hazem.booking_changes WHERE undone_at IS NOT NULL)"));

        var done = await staff.PostAsJsonAsync("/api/panel/booking/undo", new { confirm = true }, Ct);
        Assert.Equal(1, (int)(await done.Content.ReadFromJsonAsync<JsonNode>(Ct))!["cancelled"]!);
        Assert.Equal("cancelled_by_office|pending|1", await db.ScalarAsync<string>(
            "SELECT (SELECT state || '|' || client_notice FROM hazem.appointments) || '|' || (SELECT count(*) FROM hazem.booking_changes WHERE undone_at IS NOT NULL)"));
    }

    [Fact]
    public async Task With_booking_off_the_office_s_cancellation_is_the_text_alone_and_a_task()
    {
        await BookingOn();
        await UpToTheOffer();
        await Press("bk:s:pv:" + new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds());
        await db.ExecAsync("UPDATE hazem.settings SET value = 'false' WHERE key = 'booking.enabled'");
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        var id = await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments");
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/panel/booking/appointments/{id}/cancel", null, Ct)).StatusCode);
        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        var message = JsonNode.Parse(_factory.N8n.Requests.Skip(sends).Single().Body)!;
        Assert.Equal("send_text", (string?)message["type"]); // no times to press while Hazem does not book
        Assert.Equal("office_cancelled", await db.ScalarAsync<string>("SELECT type FROM hazem.staff_tasks WHERE type = 'office_cancelled'"));
    }

    [Fact]
    public async Task The_week_and_the_settings_change_what_hazem_offers_at_once()
    {
        await BookingOn();
        var staff = await SignIn("karim", Permissions.Staff, "كريم");
        var week = Enumerable.Range(0, 7).Select(d => new { weekday = d, ranges = d == 5 ? new object[0] : new object[] { new { start = 18 * 60, end = 20 * 60 } } });
        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync("/api/panel/booking/week", new { days = week }, Ct)).StatusCode);

        await UpToTheOffer();
        var first = (await Press("bk:m:of")).Single()!["interactive"]!["choices"]![0]!;
        Assert.Equal("النهارده 6:00 م", (string?)first["title"]);

        var bad = await staff.PutAsJsonAsync("/api/panel/booking/week",
            new { days = new[] { new { weekday = 1, ranges = new[] { new { start = 600, end = 900 }, new { start = 800, end = 1000 } } } } }, Ct);
        Assert.Equal("overlapping_ranges", (string?)(await bad.Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]);

        var settings = (await staff.GetFromJsonAsync<JsonNode>("/api/panel/booking/settings", Ct))!.AsObject();
        Assert.True((bool)settings["texts_approved"]!);
        settings["slot_step_minutes"] = 7;
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync("/api/panel/booking/settings", settings, Ct)).StatusCode);
        settings["slot_step_minutes"] = 30;
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PutAsJsonAsync("/api/panel/booking/settings", settings, Ct)).StatusCode);
        // the lawyer's calendar receives every appointment: a staff member cannot point it elsewhere
        var moved = settings.DeepClone().AsObject();
        moved["calendar_id"] = "somewhere-else@example.test";
        var refused = await staff.PutAsJsonAsync("/api/panel/booking/settings", moved, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("calendar_id_forbidden", (string?)(await refused.Content.ReadFromJsonAsync<JsonNode>(Ct))!["error"]);
        var preview = await staff.GetFromJsonAsync<JsonNode>("/api/panel/booking/preview?day=2026-11-21&service=pv", Ct);
        Assert.Equal(["18:00", "18:30", "19:00", "19:30"], preview!["slots"]!.AsArray()
            .Select(s => TimeZoneInfo.ConvertTime(DateTimeOffset.Parse((string)s!), Office.Domain.Tasks.OfficeHours.Cairo).ToString("HH:mm")));
    }

    [Fact]
    public async Task The_office_s_payment_details_are_the_manager_s_and_the_lawyer_s()
    {
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/panel/office", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/panel/booking/week", Ct)).StatusCode);

        var lawyer = await SignIn("saad", Permissions.Lawyer);
        var body = new { instapay = "office@instapay", wallet_number = "01000000000", wallet_provider = "فودافون كاش", address = "", map_url = "http://maps" };
        Assert.Equal(HttpStatusCode.BadRequest, (await lawyer.PutAsJsonAsync("/api/panel/office", body, Ct)).StatusCode); // a map link must be https
        Assert.Equal(HttpStatusCode.NoContent, (await lawyer.PutAsJsonAsync("/api/panel/office", body with { map_url = "https://maps.app.goo.gl/x" }, Ct)).StatusCode);
        Assert.Equal("office@instapay", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'office.instapay'"));
    }

    [Fact]
    public async Task A_calendar_that_does_not_answer_offers_nothing_and_opens_a_task()
    {
        await BookingOn();
        _factory.Google.Down = true;
        await UpToTheOffer();
        await Press("bk:m:pv");
        var answer = (await Press("bk:v:wa")).Single()!;
        Assert.Equal("send_text", (string?)answer["type"]);
        Assert.Equal("booking_unavailable", await db.ScalarAsync<string>("SELECT type FROM hazem.staff_tasks"));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.appointments"));
    }
}
