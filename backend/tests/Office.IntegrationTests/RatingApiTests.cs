using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Application.Panel;
using Office.Application.Ratings;
using Office.Infrastructure.Booking;
using Office.Infrastructure.Ratings;

namespace Office.IntegrationTests;

/// <summary>
/// v1.31 (DECISIONS B-26 / M-03) through the real API and database: the rating the worker makes 2 hours after the lawyer
/// marks a consultation completed and sends once (the list inside the 24-hour window; outside it the template, and while
/// Meta has not approved it: skipped, no task), the client's answer read by code («يحتاج للتحسين» → a task, his next words
/// its reason), and «تقييمات العملاء» in the month's report. The app's clock starts on Wednesday 18 November 2026, 10:00
/// Cairo; the client books Friday 20 November at 12:00.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class RatingApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000131";
    private const string Password = "a long test password 1";
    private static readonly DateTimeOffset Wednesday10 = new(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Friday12 = new(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Friday13 = new(2026, 11, 20, 13, 0, 0, TimeSpan.FromHours(2));
    private readonly MutableClock _clock = new(Wednesday10.ToUniversalTime());
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private HttpClient? _lawyer;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.service_ratings; DELETE FROM hazem.appointment_reminders;
        DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments; DELETE FROM hazem.appointments;
        DELETE FROM hazem.booking_exceptions; DELETE FROM hazem.booking_changes;
        UPDATE hazem.settings SET value = 'false' WHERE key = 'booking.enabled';
        UPDATE hazem.settings SET value = '' WHERE key IN ('booking.calendar_id', 'office.instapay', 'office.wallet_number', 'office.wallet_provider',
          'office.address', 'office.map_url');
        UPDATE hazem.settings SET value = 'true' WHERE key = 'rating.enabled';
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
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
            UPDATE hazem.settings SET value = '2026-01-01T00:00:00Z' WHERE key = 'rating.since';
            """);
        _factory = new ApiFactory(db, _clock);
        _n8n = _factory.CreateN8nClient();
    }

    public async ValueTask DisposeAsync()
    {
        _lawyer?.Dispose();
        _n8n.Dispose();
        await _factory.DisposeAsync();
        await db.ExecAsync(Clean);
        await db.SetAsync("hazem.recipients", "");
    }

    // ---------- helpers ----------

    private async Task<(JsonArray Actions, string TurnId)> Turn(JsonObject message)
    {
        message["wamid"] = $"wamid.RT-{++_n}";
        var turnId = $"wt:00000000-0000-0000-0131-{++_n:D12}";
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = turnId, ["wa_id"] = Client, ["messages"] = new JsonArray(message),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return ((await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray(), turnId);
    }

    private async Task<JsonArray> Say(string text) => (await Turn(new JsonObject { ["type"] = "text", ["text"] = text })).Actions;

    private async Task<JsonArray> Press(string id) =>
        (await Turn(new JsonObject { ["type"] = "interactive", ["interactive"] = new JsonObject { ["kind"] = "list_reply", ["id"] = id, ["title"] = "x" } })).Actions;

    private Task<(JsonArray Actions, string TurnId)> Receipt() => Turn(new JsonObject
    {
        ["type"] = "image",
        ["media"] = new JsonObject
        {
            ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = $"wa/2026/11/receipt-{_n}.jpg", ["mime_type"] = "image/jpeg",
            ["size_bytes"] = 3, ["sha256"] = $"r{_n}",
        },
    });

    /// <summary>The client books Friday 12:00 and sends the receipt; at 12:15 the lawyer marks it completed in the panel.</summary>
    private async Task<Guid> Completed()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        await Press($"bk:s:pv:{Friday12.ToUnixTimeSeconds()}");
        await Receipt();
        var id = await db.ScalarAsync<Guid>("SELECT id FROM hazem.appointments WHERE state = 'confirmed'");
        At(Friday12.AddMinutes(15));
        _lawyer ??= await SignIn("saad", Permissions.Lawyer);
        Assert.Equal(HttpStatusCode.NoContent, (await _lawyer.PostAsJsonAsync($"/api/panel/booking/appointments/{id}/state",
            new { state = "completed" }, Ct)).StatusCode);
        return id;
    }

    private async Task<int> Ratings()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RatingNotices>().RunAsync(Ct);
    }

    private Task<string?> Row() => db.ScalarAsync<string>("""
        SELECT string_agg(service || ':' || state || ':' || coalesce(channel, skip_reason, error_code, '-') || ':' || coalesce(score::text, '-'), ',')
          FROM hazem.service_ratings
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

    // ---------- the texts ----------

    [Fact]
    public async Task Migration_0033_added_the_rating_texts_active_within_their_limits()
    {
        var keys = string.Join(", ", RatingTexts.Keys.Append("welcome_back").Select(k => $"'{k}'"));
        Assert.Equal(11L, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.approved_texts WHERE active AND key IN ({keys})"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.approved_texts WHERE key IN ({keys}) AND char_length(text) > max_chars"));
        Assert.Equal("7|true|2|24|30", await db.ScalarAsync<string>("""
            SELECT string_agg(value, '|' ORDER BY array_position(ARRAY['intake.fresh_after_days', 'rating.enabled',
                     'rating.after_appointment_hours', 'rating.after_delivery_hours', 'rating.repeat_days'], key))
              FROM hazem.settings WHERE key IN ('intake.fresh_after_days', 'rating.enabled', 'rating.after_appointment_hours',
                                                'rating.after_delivery_hours', 'rating.repeat_days')
            """));
    }

    // ---------- the worker ----------

    [Fact]
    public async Task Two_hours_after_completed_the_list_goes_once_and_needs_improvement_is_a_task_with_its_reason()
    {
        var id = await Completed();
        Assert.Equal(0, await Ratings());
        Assert.Equal("consultation_phone_video:scheduled:-:-", await Row());
        Assert.Equal(Friday12.AddMinutes(15).AddHours(2), await db.ScalarAsync<DateTime>(
            $"SELECT due_at FROM hazem.service_ratings WHERE appointment_id = '{id}'"));

        At(Friday12.AddMinutes(15).AddHours(2));
        var from = _factory.N8n.Requests.Count;
        Assert.Equal(1, await Ratings());
        var list = Assert.Single(SentSince(from));
        Assert.Equal("send_interactive", (string)list["type"]!);
        Assert.Equal("شكرًا إن حضرتك اخترت مكتب الأستاذ سعد في الاستشارة. رأيك يهمنا عشان نحسّن خدمتنا، تقيّم الخدمة إزاي؟", (string)list["text"]!);
        var interactive = list["interactive"]!;
        Assert.Equal(("list", "قيّم الخدمة"), ((string)interactive["kind"]!, (string)interactive["button"]!));
        var rating = await db.ScalarAsync<Guid>("SELECT id FROM hazem.service_ratings");
        var rows = interactive["choices"]!.AsArray();
        Assert.Equal(["ممتاز", "جيد جدًا", "جيد", "يحتاج للتحسين", "ليس الآن"], rows.Select(r => (string)r!["title"]!));
        Assert.Equal(new int?[] { 5, 4, 3, 2, null }.Select(s => RatingButtons.Id(rating, s)), rows.Select(r => (string)r!["id"]!));
        Assert.Equal(0, await Ratings()); // once
        Assert.Equal("consultation_phone_video:sent:message:-", await Row());

        // «يحتاج للتحسين»: stored, «آسفين…» and a task for the managers.
        var low = await Press(RatingButtons.Id(rating, 2));
        Assert.StartsWith("آسفين إن التجربة ما كانتش زي ما حضرتك عايز.", (string)Assert.Single(low)!["text"]!);
        Assert.Equal("consultation_phone_video:sent:message:2", await Row());
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'rating_low'"));

        // His next words are its reason: no reply, and the task now shows them («كلام العميل»).
        var (reason, turn) = await Turn(new JsonObject { ["type"] = "text", ["text"] = "الأستاذ اتأخر عليا نص ساعة" });
        Assert.Empty(reason);
        Assert.Equal(turn, await db.ScalarAsync<string>("SELECT turn_id FROM hazem.staff_tasks WHERE type = 'rating_low'"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.service_ratings WHERE reason_at IS NOT NULL"));

        // A second press changes nothing.
        Assert.StartsWith("شكرًا لحضرتك على التقييم", (string)Assert.Single(await Press(RatingButtons.Id(rating, 5)))!["text"]!);
        Assert.Equal("consultation_phone_video:sent:message:2", await Row());
    }

    [Fact]
    public async Task Outside_the_window_without_an_approved_template_it_is_skipped_never_a_task()
    {
        await Completed();
        await Ratings();
        _factory.N8n.Responder = r => Answer(false, TypeOf(r) == "send_template" ? "TEMPLATE_NOT_APPROVED" : ReminderNotices.OutsideWindow);
        At(Friday12.AddDays(2));
        var from = _factory.N8n.Requests.Count;
        await Ratings();

        var sent = SentSince(from);
        Assert.Equal(["send_interactive", "send_template"], sent.Select(s => (string)s["type"]!));
        var t = sent[1];
        Assert.Equal(("service_rating", "ar"), ((string)t["template_name"]!, (string)t["language"]!));
        var parts = t["components"]!.AsArray();
        Assert.Equal("الاستشارة", (string)parts[0]!["parameters"]![0]!["text"]!);
        Assert.Equal(5, parts.Count - 1); // the five answers as quick replies
        Assert.Equal("consultation_phone_video:skipped:no_template:-", await Row());
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'rating_low'"));

        from = _factory.N8n.Requests.Count;
        await Ratings();
        Assert.Equal(from, _factory.N8n.Requests.Count); // never tried again
    }

    [Fact]
    public async Task Outside_the_window_an_approved_template_carries_it_and_its_quick_reply_is_read_the_same()
    {
        await Completed();
        await Ratings();
        _factory.N8n.Responder = r => TypeOf(r) == "send_template" ? Answer(true) : Answer(false, ReminderNotices.OutsideWindow);
        At(Friday12.AddHours(3));
        await Ratings();
        Assert.Equal("consultation_phone_video:sent:template:-", await Row());
        _factory.N8n.Responder = null;

        var rating = await db.ScalarAsync<Guid>("SELECT id FROM hazem.service_ratings");
        var (actions, _) = await Turn(new JsonObject
        {
            ["type"] = "button", ["button"] = new JsonObject { ["text"] = "ممتاز", ["payload"] = RatingButtons.Id(rating, 5) },
        });
        Assert.StartsWith("شكرًا لحضرتك على التقييم", (string)actions[0]!["text"]!);
        Assert.Equal("consultation_phone_video:sent:template:5", await Row());
    }

    [Fact]
    public async Task A_muted_conversation_never_gets_one()
    {
        await Completed();
        await Ratings();
        await db.ExecAsync("UPDATE hazem.conversations SET state = 'muted'");
        At(Friday13.AddHours(2));
        var from = _factory.N8n.Requests.Count;
        await Ratings();
        Assert.Equal(from, _factory.N8n.Requests.Count);
        Assert.Equal("consultation_phone_video:skipped:muted:-", await Row());
    }

    // ---------- the report ----------

    [Fact]
    public async Task The_months_report_has_the_ratings_their_average_and_the_low_ones_with_the_clients_name()
    {
        var id = await Completed();
        await Ratings();
        At(Friday13.AddHours(2));
        await Ratings();
        await Press(RatingButtons.Id(await db.ScalarAsync<Guid>("SELECT id FROM hazem.service_ratings"), 2));

        var r = (await _lawyer!.GetFromJsonAsync<JsonNode>("/api/panel/reports?month=2026-11", Ct))!["ratings"]!;
        Assert.Equal((1, 1, 2.0), ((int)r["sent"]!, (int)r["answered"]!, (double)r["average"]!));
        var service = Assert.Single(r["by_service"]!.AsArray())!;
        Assert.Equal(("consultation_phone_video", 1, 2.0), ((string)service["key"]!, (int)service["count"]!, (double)service["average"]!));
        var low = Assert.Single(r["low"]!.AsArray())!;
        Assert.Equal(("consultation_phone_video", "محمد أحمد علي حسن"), ((string)low["service"]!, (string)low["client_name"]!));

        var october = (await _lawyer!.GetFromJsonAsync<JsonNode>("/api/panel/reports?month=2026-10", Ct))!["ratings"]!;
        Assert.Equal((0, 0), ((int)october["sent"]!, (int)october["answered"]!));
        Assert.Null(october["average"]);
        Assert.NotEqual(Guid.Empty, id);
    }
}
