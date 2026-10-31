using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Application.Panel;
using Office.Infrastructure.Panel;

namespace Office.IntegrationTests;

/// <summary>
/// «أدوات التجربة» (the owner, 2026-10-30) through the real API and database: the manager and the lawyer make Hazem
/// forget one of his test numbers, never any other number; the staff cannot; Chatwoot loses the contact too.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class PanelTestingApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Password = "a long test password 1";
    private const string Tester = "201000000071", Other = "201000000072", Client = "201000000073";
    private ApiFactory _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync($"""
            TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
            DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons WHERE wa_id IN ('{Tester}', '{Other}', '{Client}');
            INSERT INTO hazem.persons (id, wa_id, name_raw, full_name_provided) VALUES
              (gen_random_uuid(), '{Tester}', 'تجربة', true), (gen_random_uuid(), '{Client}', 'عميل', true);
            """);
        await db.SetAsync("hazem.recipients", $"{Tester},{Other}");
        _factory = new ApiFactory(db) { ChatwootConfigured = true };
        _factory.Chatwoot.Contacts[Tester] = 501;
        await NewUser("boss", Permissions.Manager);
        await NewUser("lawyer", Permissions.Lawyer);
        await NewUser("yasmin", Permissions.Staff);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await db.SetAsync("hazem.recipients", "");
        await db.ExecAsync($"TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users; DELETE FROM hazem.persons WHERE wa_id = '{Client}';");
    }

    private async Task NewUser(string name, string role)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PanelAccounts>().CreateAsync(null,
            new PanelAccounts.NewAccount(name, name, $"{name}@example.test", role, role == Permissions.Staff ? name : null), Password, Ct);
    }

    private async Task<HttpClient> SignIn(string user)
    {
        var c = _factory.CreateClient();
        await Token(c);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/panel/auth/login", new { username = user, password = Password }, Ct)).StatusCode);
        await Token(c);
        return c;
    }

    private static async Task Token(HttpClient c)
    {
        var body = await c.GetFromJsonAsync<JsonNode>("/api/panel/auth/csrf", Ct);
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (string)body!["token"]!);
    }

    [Fact]
    public async Task The_manager_sees_the_test_numbers_masked_and_forgets_one_with_its_Chatwoot_contact()
    {
        var boss = await SignIn("boss");
        var list = await boss.GetFromJsonAsync<JsonNode>("/api/panel/testing/numbers", Ct);
        var numbers = list!["numbers"]!.AsArray();
        Assert.Equal(2, numbers.Count);
        Assert.DoesNotContain(Tester, list.ToJsonString(), StringComparison.Ordinal); // only «*********071» and a key
        Assert.Equal("*********071", (string)numbers[0]!["masked"]!);

        var r = await boss.PostAsJsonAsync("/api/panel/testing/forget", new { key = PanelTesting.KeyOf(Tester) }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonNode>(Ct);
        Assert.True((int)body!["rows"]! >= 1);
        Assert.Equal("ok", (string)body["chatwoot"]!);
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.persons WHERE wa_id = '{Tester}'"));
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.persons WHERE wa_id = '{Client}'"));
        Assert.Contains(_factory.Chatwoot.Calls, c => c.Method == "DELETE" && c.Path == $"/api/v1/accounts/{FakeChatwoot.Account}/contacts/501");
        Assert.Contains(_factory.Chatwoot.Calls, c => c.Method == "DELETE" && c.Path.EndsWith("/conversations/601", StringComparison.Ordinal));
        Assert.Equal("testing.forget", await db.ScalarAsync<string>("SELECT action FROM hazem.panel_audit WHERE action = 'testing.forget'"));
    }

    [Fact]
    public async Task Only_a_test_number_can_be_forgotten_and_only_by_the_manager_or_the_lawyer()
    {
        var lawyer = await SignIn("lawyer");
        // a client's number is not in hazem.recipients: its key matches nothing
        var r = await lawyer.PostAsJsonAsync("/api/panel/testing/forget", new { key = PanelTesting.KeyOf(Client) }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.persons WHERE wa_id = '{Client}'"));
        Assert.Equal(HttpStatusCode.OK, (await lawyer.GetAsync("/api/panel/testing/numbers", Ct)).StatusCode);

        var staff = await SignIn("yasmin");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/panel/testing/numbers", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync("/api/panel/testing/forget", new { key = PanelTesting.KeyOf(Tester) }, Ct)).StatusCode);
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM hazem.persons WHERE wa_id = '{Tester}'"));
    }

    [Fact]
    public async Task V134_the_lawyer_switches_the_urgent_alert_inside_the_window_and_the_staff_cannot()
    {
        try
        {
            var lawyer = await SignIn("lawyer");
            Assert.False((bool)(await lawyer.GetFromJsonAsync<JsonNode>("/api/panel/testing/urgent-window", Ct))!["on"]!); // off by default
            Assert.Equal(HttpStatusCode.BadRequest, (await lawyer.PutAsJsonAsync("/api/panel/testing/urgent-window", new { }, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await lawyer.PutAsJsonAsync("/api/panel/testing/urgent-window", new { on = true }, Ct)).StatusCode);
            Assert.Equal("true", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'urgent.alert_in_window'"));
            Assert.Equal("true", await db.ScalarAsync<string>(
                "SELECT detail ->> 'on' FROM hazem.panel_audit WHERE action = 'testing.urgent_window' AND target_type = 'booking'"));

            var staff = await SignIn("yasmin");
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/panel/testing/urgent-window", new { on = false }, Ct)).StatusCode);
            Assert.Equal("true", await db.ScalarAsync<string>("SELECT value FROM hazem.settings WHERE key = 'urgent.alert_in_window'"));
        }
        finally
        {
            await db.SetAsync("urgent.alert_in_window", "false");
        }
    }
}
