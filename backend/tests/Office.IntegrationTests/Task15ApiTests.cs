using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Infrastructure.Panel;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 15 (DECISIONS v1.37) through the real API and database: a client whose WhatsApp has a username comes without his
/// number; his key is Meta's business-scoped user id (BSUID). Hazem answers him when «@username» is a test recipient,
/// keeps his username, sends to the BSUID, shows «من غير رقم» for him, and the testing tools forget him by «@username».
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class Task15ApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Bsuid = "EG.15151515151515151515";
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks;
        DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
        """;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync(Clean);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", "201000000151,@Tester15");
        _factory = new ApiFactory(db);
        _n8n = _factory.CreateN8nClient();
    }

    public async ValueTask DisposeAsync()
    {
        _n8n.Dispose();
        await _factory.DisposeAsync();
        await db.ExecAsync(Clean);
        await db.SetAsync("hazem.recipients", "");
    }

    private async Task<JsonArray> Say(string waId, string? username, string text)
    {
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0015-{++_n:D12}", ["wa_id"] = waId, ["contact_name"] = "Tester",
            ["contact_username"] = username,
            ["messages"] = new JsonArray(new JsonObject { ["wamid"] = $"wamid.T15-{_n}", ["type"] = "text", ["text"] = text }),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray();
    }

    [Fact]
    public async Task A_username_client_is_answered_by_his_bsuid_and_kept_with_his_username()
    {
        var welcome = await Say(Bsuid, "tester15", "السلام عليكم"); // «@Tester15» in the list: case does not matter
        Assert.NotEmpty(welcome);
        // The replies go back to n8n for this turn, which sends them to the BSUID with «recipient» (META-WA-SEND-MESSAGE).
        Assert.Equal($"{Bsuid}|tester15|Tester (اسم الواتساب)", await db.ScalarAsync<string>(
            $"SELECT wa_id || '|' || whatsapp_username || '|' || office_name FROM hazem.persons WHERE wa_id = '{Bsuid}'"));

        // Another username, not in the test list: no answer, nothing saved for him.
        Assert.Empty(await Say("EG.99999999999999999999", "stranger", "السلام عليكم"));
    }

    [Fact]
    public async Task The_testing_tools_list_and_forget_a_username_client()
    {
        await Say(Bsuid, "tester15", "السلام عليكم");
        using var scope = _factory.Services.CreateScope();
        var testing = scope.ServiceProvider.GetRequiredService<PanelTesting>();
        Assert.Contains(await testing.NumbersAsync(Ct), n => n.Masked == "@Tester15"); // as written, not masked
        var by = Guid.NewGuid();
        await db.ExecAsync($"""
            INSERT INTO hazem.panel_users (id, username, display_name, email, role, password_hash, security_stamp)
            VALUES ('{by}', 't15.boss', 'Boss', 't15@example.test', 'manager', '{new string('x', 40)}', '{new string('s', 20)}')
            """);
        var forgot = await testing.ForgetAsync(by, PanelTesting.KeyOf("@Tester15"), Ct);
        Assert.Equal("@Tester15", forgot!.Masked);
        Assert.True(forgot.Rows >= 1);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.persons WHERE wa_id = '{Bsuid}'"));
    }

    [Fact]
    public void The_call_number_and_the_calendar_show_from_ghayr_raqm_for_a_bsuid_without_a_given_number()
    {
        Assert.Equal("من غير رقم", Office.Application.Booking.UrgentTexts.CallNumber(Bsuid, null));
        Assert.Equal("01000000152", Office.Application.Booking.UrgentTexts.CallNumber(Bsuid, "201000000152"));
        Assert.Equal("01000000151", Office.Application.Booking.UrgentTexts.CallNumber("201000000151", null));
        var row = new Office.Infrastructure.Persistence.AppointmentRow
        {
            Service = "consultation_office", State = "confirmed", BookedTurnId = "wt:1", CalendarState = "synced",
        };
        Assert.Contains("– من غير رقم –", Office.Infrastructure.Booking.CalendarSync.Summary(row, "س", Bsuid, null));
    }
}
