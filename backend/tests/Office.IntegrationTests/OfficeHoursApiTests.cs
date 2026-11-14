using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Infrastructure.Lists;

namespace Office.IntegrationTests;

/// <summary>
/// B-06 due times through the real API and database, at fixed hours. Found on 2026-10-23: at night
/// the due time is a Cairo time ("today/tomorrow 10:00", offset +03:00) and Npgsql refused to write
/// it, so every turn that opened a task between 23:00 and 10:00 failed with 500 and the client got
/// no answer until the morning. The tests used to pass only because they ran in the day.
/// v1.115: B-06 follows the office's hours of the panel (10:00–20:00, 0085), and outside them the client hears when the office
/// answers (0086). Found on 2026-11-14 the same way: two tests on the machine's clock failed in the CI at 22:29 Cairo.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class OfficeHoursApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Listed = "201000000091";
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync("""
            TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
            TRUNCATE hazem.turns, hazem.ai_usage, hazem.generated_replies, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.staff_task_turns, hazem.list_entries;
            DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
            """);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", Listed);
    }

    public async ValueTask DisposeAsync() => await db.SetAsync("hazem.recipients", "");

    // Instants in UTC; Cairo is +03:00 on these dates (summer time ends on 2026-10-29). The day he hears, or none.
    [Theory]
    [InlineData("2026-10-22T22:30:00Z", "2026-10-23T07:00:00Z", "النهارده")] // 01:30 Cairo → today 10:00
    [InlineData("2026-10-22T20:30:00Z", "2026-10-23T07:00:00Z", "بكرة")] // 23:30 Cairo → tomorrow 10:00
    [InlineData("2026-10-22T16:30:00Z", "2026-10-23T07:00:00Z", "بكرة")] // v1.115: 19:30 Cairo, the office's last hour → tomorrow 10:00
    [InlineData("2026-10-23T06:59:00Z", "2026-10-23T07:00:00Z", "النهارده")] // 09:59 Cairo → today 10:00
    [InlineData("2026-10-23T09:00:00Z", "2026-10-23T10:00:00Z", null)] // 12:00 Cairo → within an hour, nothing added
    public async Task A_task_opened_at_any_hour_is_saved_with_its_B06_due_time_and_outside_the_hours_he_hears_when(
        string now, string due, string? day)
    {
        await db.SetAsync("booking.office_start_minute", "600"); // 0085's hours: another class empties them
        await db.SetAsync("booking.office_end_minute", "1200");
        await using var factory = new ApiFactory(db, new FixedClock(DateTimeOffset.Parse(now)));
        using var n8n = factory.CreateN8nClient();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ListImporter>().ImportAsync(
                "existing_client", new StringReader($"name,phone,staff\nمحمد أحمد علي حسن,{Listed},أ. محمود\n"), false, Ct);
        }

        var r = await n8n.PostAsJsonAsync("/api/hazem/turns", Turn("الجلسة الجاية امتى؟"), Ct);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(due, await db.ScalarAsync<string>(Utc("due_at", "hazem.staff_tasks")));
        List<string?> said = ["رسالة حضرتك وصلت للمكتب، وهتتبلّغ للمسؤول عن موضوعك."];
        if (day is not null)
        {
            said.Add($"المكتب بيرد من 10 الصبح لـ 8 بالليل، فحد من المكتب هيرد على حضرتك هنا {day} من الساعة 10 الصبح 🙏");
        }

        Assert.Equal(said, (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray().Select(a => (string?)a!["text"]));
    }

    [Fact]
    public async Task A_staff_event_with_a_cairo_offset_is_saved()
    {
        await using var factory = new ApiFactory(db, new FixedClock(DateTimeOffset.Parse("2026-10-22T22:30:00Z")));
        using var n8n = factory.CreateN8nClient();
        Assert.Equal(HttpStatusCode.OK, (await n8n.PostAsJsonAsync("/api/hazem/turns", Turn("السلام عليكم"), Ct)).StatusCode);

        var r = await n8n.PostAsJsonAsync("/api/hazem/staff-events",
            new { wa_id = Listed, @event = "staff_replied", at = "2026-10-23T01:15:00+03:00" }, Ct);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("2026-10-22T22:15:00Z", await db.ScalarAsync<string>(Utc("last_staff_message_at", "hazem.conversations")));
    }

    private static string Utc(string column, string table) =>
        $"SELECT to_char({column} AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"') FROM {table}";

    private JsonObject Turn(string text) => new()
    {
        ["turn_id"] = $"wt:00000000-0000-0000-0009-{++_n:D12}",
        ["wa_id"] = Listed,
        ["messages"] = new JsonArray(new JsonObject { ["wamid"] = $"wamid.HOURS-{_n}", ["type"] = "text", ["text"] = text }),
    };
}
