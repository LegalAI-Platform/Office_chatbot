using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Office.Application.Panel;
using Office.Infrastructure.Booking;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 09 (DECISIONS v1.13) through the real API and database: the hold until the receipt image, the worker's
/// reminder and expiry, the office's review and refunds (every role, never the system), and B-22.
/// The app's clock stands at Thursday 19 November 2026, 12:00 Cairo; the database's own now() is the real one,
/// so a test that needs a hold to run out or a receipt's time moves it in SQL.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class Task09ApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000092";
    private const string Password = "a long test password 1";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly string Tomorrow12 = "bk:s:pv:" + new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds();
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments; DELETE FROM hazem.appointments;
        DELETE FROM hazem.booking_exceptions; DELETE FROM hazem.booking_changes;
        UPDATE hazem.settings SET value = 'false' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = '' WHERE key IN ('booking.calendar_id', 'office.instapay', 'office.wallet_number', 'office.wallet_provider',
                                                          'office.address', 'office.map_url', 'office.phone', 'office.meet_link');
        UPDATE hazem.settings SET value = '60' WHERE key = 'booking.review_alert_minutes';
        UPDATE hazem.approved_texts SET active = true WHERE source IN ('proposal TASK 08', 'proposal TASK 09') AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.generated_replies, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
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

    /// <summary>Booking on: the texts (approved since 0013 and 0014), the office's details, the calendar id.</summary>
    private Task BookingOn() => db.ExecAsync("""
        UPDATE hazem.approved_texts SET active = true WHERE source = 'proposal TASK 09' AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
        UPDATE hazem.settings SET value = 'true' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = 'lawyer@example.test' WHERE key = 'booking.calendar_id';
        UPDATE hazem.settings SET value = 'office@instapay' WHERE key = 'office.instapay';
        UPDATE hazem.settings SET value = '01000000000' WHERE key = 'office.wallet_number';
        UPDATE hazem.settings SET value = 'فودافون كاش' WHERE key = 'office.wallet_provider';
        """);

    private async Task<JsonArray> Turn(JsonObject message)
    {
        message["wamid"] = $"wamid.T09-{++_n}";
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0009-{++_n:D12}", ["wa_id"] = Client, ["messages"] = new JsonArray(message),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray();
    }

    private Task<JsonArray> Say(string text) => Turn(new JsonObject { ["type"] = "text", ["text"] = text });

    private Task<JsonArray> Press(string id) =>
        Turn(new JsonObject { ["type"] = "interactive", ["interactive"] = new JsonObject { ["kind"] = "list_reply", ["id"] = id, ["title"] = "x" } });

    /// <summary>A receipt image n8n stored, and its bytes in the (fake) storage the panel reads.</summary>
    private Task<JsonArray> Receipt(string sha = "r1")
    {
        var key = $"wa/2026/11/receipt-{_n}.jpg";
        _factory.Files.Objects[$"office-sensitive/{key}"] = ([0xFF, 0xD8, 0xFF], "image/jpeg");
        return Turn(new JsonObject
        {
            ["type"] = "image",
            ["media"] = new JsonObject
            {
                ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = key, ["mime_type"] = "image/jpeg", ["size_bytes"] = 3, ["sha256"] = sha,
            },
        });
    }

    private async Task<Guid> HeldTomorrow()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        await Press(Tomorrow12);
        return await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments WHERE state = 'pending_payment'");
    }

    private async Task<int> Notices()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BookingNotices>().RunAsync(Ct);
    }

    /// <summary>The texts of what the worker sent since <paramref name="from"/>.</summary>
    private List<string> SentSince(int from) =>
        _factory.N8n.Requests.Skip(from).Select(r => (string)JsonNode.Parse(r.Body)!["text"]!).ToList();

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

    // ---------- the texts ----------

    [Fact]
    public async Task Migration_0014_activated_the_15_texts_the_owner_approved_and_the_booking_needs_them_all()
    {
        // 1.25: booking_paid v2 (the time kept until the review) replaced v1; 1.32: one more got its «المستشار» version (0034)
        Assert.Equal("13|15", await db.ScalarAsync<string>(
            """
            SELECT count(*) FILTER (WHERE active) || '|' || count(*) FROM hazem.approved_texts
             WHERE source = 'proposal TASK 09' AND updated_by = 'owner approval 2026-10-24'
            """));
        Assert.Equal(0L, await db.ScalarAsync<long>(
            "SELECT count(*) FROM hazem.approved_texts WHERE source = 'proposal TASK 09' AND char_length(text) > max_chars"));

        await BookingOn();
        await db.ExecAsync("UPDATE hazem.approved_texts SET active = false WHERE source = 'proposal TASK 09'");
        await Say("السلام عليكم");
        var offer = await Say("محمد أحمد علي حسن");
        Assert.DoesNotContain(offer.Where(a => (string?)a!["type"] == "send_interactive").SelectMany(a => a!["interactive"]!["choices"]!.AsArray()),
            c => ((string?)c!["id"])!.StartsWith("bk:", StringComparison.Ordinal)); // v1.30: the services list, without the booking's buttons
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.False((bool)(await staff.GetFromJsonAsync<JsonNode>("/api/panel/booking/settings", Ct))!["texts_approved"]!);
    }

    // ---------- the hold ----------

    [Fact]
    public async Task The_worker_reminds_once_then_frees_the_time_and_tells_the_client()
    {
        await BookingOn();
        var id = await HeldTomorrow();
        await db.ExecAsync($"UPDATE hazem.appointments SET created_at = now() - interval '2 hours', hold_until = now() + interval '20 minutes' WHERE id = '{id}'");

        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        Assert.Equal(0, await Notices()); // once
        var reminder = Assert.Single(SentSince(sends));
        Assert.StartsWith("⏰ فاضل نص ساعة: ميعاد حضرتك بكرة الساعة 12:00 م", reminder);
        Assert.Contains("01000000000 (فودافون كاش)", reminder);

        await db.ExecAsync($"UPDATE hazem.appointments SET hold_until = now() - interval '1 minute' WHERE id = '{id}'");
        sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        Assert.Equal("expired|sent", await db.ScalarAsync<string>($"SELECT state || '|' || client_notice FROM hazem.appointments WHERE id = '{id}'"));
        Assert.StartsWith("❌ ميعاد بكرة الساعة 12:00 م اتلغى", Assert.Single(SentSince(sends)));

        // The time is free again: somebody else may hold it.
        await db.ExecAsync("INSERT INTO hazem.persons (id, wa_id) VALUES ('00000000-0000-0000-0000-0000000009b1', '201000000901')");
        Assert.Equal("held", await db.ScalarAsync<string>("""
            SELECT hazem.book_appointment(gen_random_uuid(), '00000000-0000-0000-0000-0000000009b1', NULL, 'consultation_phone_video',
              '2026-11-20 12:00+02', '2026-11-20 12:15+02', 500, 't-other', 10, NULL, now() + interval '2 hours')->>'status'
            """));
    }

    [Fact]
    public async Task One_hold_per_client_even_without_the_function()
    {
        await BookingOn();
        var id = await HeldTomorrow();
        var e = await Assert.ThrowsAsync<PostgresException>(() => db.ExecAsync($"""
            INSERT INTO hazem.appointments (id, person_id, service, starts_at, ends_at, amount, booked_turn_id, state, calendar_state, hold_until)
            SELECT gen_random_uuid(), person_id, 'consultation_office', '2026-11-25 12:00+02', '2026-11-25 12:30+02', 1000, 't-second',
                   'pending_payment', 'wait', now() + interval '1 hour' FROM hazem.appointments WHERE id = '{id}'
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, e.SqlState);
    }

    [Fact]
    public async Task The_same_image_on_another_booking_is_flagged_and_still_confirms()
    {
        await BookingOn();
        await HeldTomorrow();
        await Receipt(sha: "same");
        await Press("bk:s:of:" + new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds());
        await Receipt(sha: "same");
        Assert.Equal("consultation_office|confirmed|duplicate_suspected", await db.ScalarAsync<string>("""
            SELECT a.service || '|' || a.state || '|' || p.state FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
             WHERE p.state = 'duplicate_suspected'
            """));
    }

    [Fact]
    public async Task All_the_payments_list_the_newest_first_and_the_review_the_oldest_first()
    {
        await BookingOn();
        await HeldTomorrow();
        await Receipt(sha: "first");
        await Press("bk:s:of:" + new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds());
        await Receipt(sha: "second");
        await db.ExecAsync("""
            UPDATE hazem.payments p SET proof_at = CASE WHEN a.service = 'consultation_office' THEN '2026-11-19 11:00+02'::timestamptz ELSE '2026-11-19 09:00+02'::timestamptz END
              FROM hazem.appointments a WHERE a.id = p.appointment_id
            """);
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");

        // the 300 cap of «الكل» then drops the oldest in a busy period, never what just came
        var all = (await staff.GetFromJsonAsync<JsonArray>("/api/panel/payments?show=all", Ct))!;
        Assert.Equal(["consultation_office", "consultation_phone_video"], all.Select(p => (string)p!["service"]!));
        var review = (await staff.GetFromJsonAsync<JsonArray>("/api/panel/payments?show=review", Ct))!;
        Assert.Equal(["consultation_phone_video", "consultation_office"], review.Select(p => (string)p!["service"]!));
    }

    // ---------- 1.25 (B-08, B-23): only the review confirms; where the lawyer calls; the late review ----------

    [Fact]
    public async Task Only_the_staff_review_confirms_and_says_where_the_lawyer_calls_once()
    {
        await BookingOn();
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        var apps = (await Press("bk:m:pv")).Single()!["interactive"]!["choices"]!.AsArray();
        Assert.Equal(["bk:v:wa", "bk:v:ft"], apps.Select(c => (string?)c!["id"])); // no Meet without its link
        await Press("bk:v:ft");
        await Press(Tomorrow12 + ":ft");
        var id = await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments WHERE state = 'pending_payment' AND call_app = 'facetime'");

        var kept = (string?)(await Receipt()).Single()!["text"];
        Assert.StartsWith("وصلت صورة التحويل ✅، والميعاد محجوز لحضرتك", kept);
        Assert.EndsWith("أول ما المكتب يراجع التحويل، هبعتلك التأكيد.", kept);
        Assert.Equal(0, await Notices()); // nothing more before the review

        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review",
            new { state = "verified", received = 500 }, Ct)).StatusCode);
        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        var confirmed = Assert.Single(SentSince(sends));
        Assert.StartsWith("تم تأكيد الاستشارة بكرة الساعة", confirmed);
        Assert.EndsWith("✅، والمستشار هيكلم حضرتك على FaceTime على نفس الرقم ده.", confirmed);

        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review",
            new { state = "verified", received = 500 }, Ct)).StatusCode);
        Assert.Equal(0, await Notices()); // the same «تمام» again tells nothing
        Assert.Equal("استلمت الصورة ✅", (string?)(await Receipt(sha: "r2")).Single()!["text"]); // and a new image is not «waiting» again
        Assert.Equal("facetime", (string?)(await staff.GetFromJsonAsync<JsonArray>("/api/panel/booking/appointments?from=2026-11-20&to=2026-11-20", Ct))!
            .Single()!["call_app"]);
    }

    [Fact]
    public async Task An_office_meeting_is_confirmed_with_the_address_the_map_and_the_phone()
    {
        await BookingOn();
        await db.ExecAsync("""
            UPDATE hazem.settings SET value = '١٢ شارع التحرير، الدقي' WHERE key = 'office.address';
            UPDATE hazem.settings SET value = 'https://maps.example.test/office' WHERE key = 'office.map_url';
            UPDATE hazem.settings SET value = '0233334444' WHERE key = 'office.phone';
            UPDATE hazem.settings SET value = 'https://meet.google.com/abc-defg-hij' WHERE key = 'office.meet_link';
            """);
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        Assert.Equal("bk:v:gm", (string?)(await Press("bk:m:pv")).Single()!["interactive"]!["choices"]![2]!["id"]); // Meet, with its link
        await Press("bk:s:of:" + new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds());
        var id = await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments WHERE state = 'pending_payment' AND call_app IS NULL");
        await Receipt();

        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review",
            new { state = "verified", received = 1000 }, Ct)).StatusCode);
        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        var confirmed = Assert.Single(SentSince(sends));
        Assert.StartsWith("تم تأكيد المقابلة بكرة الساعة", confirmed);
        Assert.EndsWith("✅، المكتب في ١٢ شارع التحرير، الدقي، وده اللوكيشن https://maps.example.test/office. لو احتجت حاجة كلمنا على 0233334444.", confirmed);
    }

    [Fact]
    public async Task A_transfer_not_reviewed_before_its_appointment_is_an_urgent_task_and_the_time_stays()
    {
        await BookingOn();
        await db.ExecAsync("UPDATE hazem.settings SET value = '2000' WHERE key = 'booking.review_alert_minutes'"); // tomorrow is «soon»
        var id = await HeldTomorrow();
        await Receipt();
        await Notices();
        await Notices();
        Assert.Equal("payment_review_due|1", await db.ScalarAsync<string>(
            "SELECT type || '|' || count(*) FROM hazem.staff_tasks WHERE type = 'payment_review_due' GROUP BY type")); // once
        Assert.Equal("confirmed|whatsapp", await db.ScalarAsync<string>($"SELECT state || '|' || call_app FROM hazem.appointments WHERE id = '{id}'"));
    }

    // ---------- the review and the refund (P1, P2, P4) ----------

    [Fact]
    public async Task The_staff_review_partial_then_more_than_due_and_record_the_refund()
    {
        await BookingOn();
        var id = await HeldTomorrow();
        await Receipt();
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");

        var waiting = (await staff.GetFromJsonAsync<JsonArray>("/api/panel/payments", Ct))!;
        Assert.Equal(("proof_received", 1, 500), ((string?)waiting.Single()!["state"], (int)waiting[0]!["receipts"]!, (int)waiting[0]!["amount_due"]!));
        var image = await staff.GetAsync($"/api/panel/payments/{id}/receipts/0", Ct);
        Assert.Equal(("image/jpeg", "no-store"), (image.Content.Headers.ContentType?.MediaType, image.Headers.CacheControl?.ToString()));
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/panel/payments/{id}/receipts/1", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review",
            new { state = "verified", received = 400 }, Ct)).StatusCode); // verified is the full amount at least
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review",
            new { state = "partial", received = 300 }, Ct)).StatusCode);
        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        var partial = Assert.Single(SentSince(sends));
        Assert.Contains("وصلنا 300 جنيه من 500 جنيه", partial);
        Assert.Contains("باقي المبلغ 200 جنيه", partial);

        Assert.StartsWith("استلمنا صورة التحويل ✅", (string?)(await Receipt(sha: "r2")).Single()!["text"]); // the rest
        Assert.Equal("proof_received", await db.ScalarAsync<string>("SELECT state FROM hazem.payments"));

        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review",
            new { state = "verified", received = 600 }, Ct)).StatusCode);
        Assert.Equal("refund_requested|100|overpaid", await db.ScalarAsync<string>(
            "SELECT state || '|' || refund_due || '|' || refund_reason FROM hazem.payments"));
        Assert.Equal("refund", await db.ScalarAsync<string>("SELECT type FROM hazem.staff_tasks WHERE type = 'refund'"));
        Assert.Single((await staff.GetFromJsonAsync<JsonArray>("/api/panel/payments?show=refunds", Ct))!);

        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/refunded",
            new { method = "فودافون كاش", note = "من رقم المكتب" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/refunded",
            new { method = "فودافون كاش" }, Ct)).StatusCode); // once
        sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        Assert.Equal("✅ تم رد 100 جنيه لحضرتك على فودافون كاش.", Assert.Single(SentSince(sends)));

        Assert.Equal("payment.receipt_open|payment.partial|payment.verified|payment.refunded", await db.ScalarAsync<string>(
            "SELECT string_agg(action, '|' ORDER BY id) FROM hazem.panel_audit WHERE target_type = 'payment'"));
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/panel/payments", Ct)).StatusCode);
    }

    // ---------- the PR #10 review ----------

    [Fact]
    public async Task A_receipt_marked_not_received_never_pays_the_next_booking()
    {
        await BookingOn();
        var id = await HeldTomorrow();
        await Receipt();
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review", new { state = "not_received" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/panel/booking/appointments/{id}/cancel", null, Ct)).StatusCode);

        var next = "bk:s:pv:" + new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds();
        Assert.StartsWith("⏳", (string?)(await Press(next)).Single()!["text"]); // held again, waiting for a real transfer
        Assert.Equal("pending_payment", await db.ScalarAsync<string>("SELECT state FROM hazem.appointments WHERE starts_at = '2026-11-22 12:00+02'"));
    }

    [Fact]
    public async Task A_refund_press_whose_money_pays_another_booking_promises_nothing()
    {
        await BookingOn();
        var paid = await HeldTomorrow();
        await Receipt();
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/panel/booking/appointments/{paid}/cancel", null, Ct)).StatusCode);
        await Press("bk:s:pv:" + new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds()); // the receipt moves here

        var reply = await Press($"bk:f:{paid:N}"); // the old «استرداد كامل» row
        Assert.DoesNotContain(reply, a => ((string?)a!["text"] ?? "").Contains("بطلب الاسترداد", StringComparison.Ordinal));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'refund'"));
        Assert.Equal("proof_received", await db.ScalarAsync<string>("SELECT state FROM hazem.payments WHERE proof_at IS NOT NULL"));
    }

    [Fact]
    public async Task Overpaid_then_cancelled_within_24_hours_gives_everything_back()
    {
        await BookingOn();
        var id = await HeldTomorrow();
        await Receipt();
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review", new { state = "verified", received = 600 }, Ct);
        await db.ExecAsync("UPDATE hazem.payments SET proof_at = '2026-11-19 11:30+02'");
        await Press($"bk:c:{id:N}");
        Assert.Equal("refund_requested|600|client_within_24h", await db.ScalarAsync<string>(
            "SELECT state || '|' || refund_due || '|' || refund_reason FROM hazem.payments"));
    }

    [Fact]
    public async Task A_replaced_hold_never_takes_a_later_receipt()
    {
        await BookingOn();
        var first = await HeldTomorrow();
        var second = "bk:s:pv:" + new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds();
        await Press(second); // replaces the first
        var replaced = await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments WHERE state = 'pending_payment'");
        await Press($"bk:c:{replaced:N}");
        Assert.Equal("cancelled_by_client", await db.ScalarAsync<string>($"SELECT state FROM hazem.appointments WHERE id = '{first}'"));

        await Receipt(); // a photo later: nothing waits for it
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.appointments WHERE state = 'confirmed'"));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.payment_receipts"));
    }

    [Fact]
    public async Task A_review_of_a_cancelled_booking_tells_the_client_nothing()
    {
        await BookingOn();
        var id = await HeldTomorrow();
        await Receipt();
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/panel/booking/appointments/{id}/cancel", null, Ct)).StatusCode);
        await Notices();
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsJsonAsync($"/api/panel/payments/{id}/review",
            new { state = "partial", received = 300 }, Ct)).StatusCode);
        Assert.Equal("partial|", await db.ScalarAsync<string>("SELECT state || '|' || COALESCE(client_notice, '') FROM hazem.payments"));
        Assert.Equal(0, await Notices());
    }

    [Fact]
    public async Task A_receipt_that_cannot_be_read_is_not_recorded_as_opened()
    {
        await BookingOn();
        var id = await HeldTomorrow();
        await Receipt();
        _factory.Files.Objects.Clear(); // gone from storage
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/panel/payments/{id}/receipts/0", Ct)).StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_audit WHERE action = 'payment.receipt_open'"));
    }

    // ---------- B-22 ----------

    [Fact]
    public async Task Cancelling_within_24_hours_of_the_receipt_opens_a_full_refund()
    {
        await BookingOn();
        var id = await HeldTomorrow();
        await Receipt();
        await db.ExecAsync("UPDATE hazem.payments SET proof_at = '2026-11-19 11:30+02'"); // the app's clock; the database's now() is real
        var reply = (await Press($"bk:c:{id:N}")).Single()!;
        Assert.StartsWith("تم إلغاء الحجز. وبما إن الإلغاء خلال 24 ساعة", (string?)reply["text"]);
        Assert.Equal("cancelled_by_client|refund_requested|500|client_within_24h", await db.ScalarAsync<string>("""
            SELECT a.state || '|' || p.state || '|' || p.refund_due || '|' || p.refund_reason
              FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
            """));
        Assert.Equal("refund", await db.ScalarAsync<string>("SELECT type FROM hazem.staff_tasks WHERE type = 'refund'"));
    }

    [Fact]
    public async Task The_office_cancels_a_hold_another_time_only_and_a_paid_booking_moves_without_paying_again()
    {
        await BookingOn();
        var hold = await HeldTomorrow();
        var staff = await SignIn("yasmin", Permissions.Staff, "ياسمين");
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/panel/booking/appointments/{hold}/cancel", null, Ct)).StatusCode);
        var sends = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Notices());
        var unpaid = JsonNode.Parse(_factory.N8n.Requests.Skip(sends).Single().Body)!;
        Assert.StartsWith("نعتذر لحضرتك، المستشار عنده ظرف في ميعاد بكرة الساعة 12:00 م", (string?)unpaid["text"]);
        Assert.DoesNotContain(unpaid["interactive"]!["choices"]!.AsArray(), r => ((string?)r!["id"])!.StartsWith("bk:f:", StringComparison.Ordinal));

        var second = "bk:s:pv:" + new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds();
        await Press(second);
        await Receipt();
        var paid = await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments WHERE state = 'confirmed'");
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/panel/booking/appointments/{paid}/cancel", null, Ct)).StatusCode);
        await Notices();

        var third = "bk:s:pv:" + new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds();
        Assert.StartsWith("وصلت صورة التحويل ✅", (string?)(await Press(third)).Single()!["text"]); // the receipt he sent pays for it
        Assert.Equal("confirmed|proof_received|1", await db.ScalarAsync<string>("""
            SELECT a.state || '|' || p.state || '|' || (SELECT count(*) FROM hazem.payment_receipts)
              FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
             WHERE a.starts_at = '2026-11-23 12:00+02'
            """));
    }
}
