using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Office.Application.Chatwoot;
using Office.Application.Panel;
using Office.Infrastructure.Chatwoot;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 07b (DECISIONS T-24) through the real API and database, with Chatwoot and n8n faked at the
/// HTTP boundary: turns copied into Chatwoot, a staff reply sent once through n8n, «resolve» hands
/// back to Hazem, and the panel's accounts in Chatwoot.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class ChatwootApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000061";
    private const string Password = "a long test password 1";
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync("""
            TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
            TRUNCATE hazem.turns, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
            TRUNCATE hazem.chatwoot_threads, hazem.chatwoot_turns, hazem.chatwoot_replies;
            DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
            UPDATE hazem.settings SET value = to_char((now() - interval '1 minute') AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
             WHERE key = 'chatwoot.mirror_since';
            """);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", Client);
        ChatwootApi.Forget();
        _factory = new ApiFactory(db) { ChatwootConfigured = true };
        _n8n = _factory.CreateN8nClient();
    }

    public async ValueTask DisposeAsync()
    {
        _n8n.Dispose();
        await _factory.DisposeAsync();
        await db.SetAsync("hazem.recipients", "");
        await db.ExecAsync("TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;");
        ChatwootApi.Forget();
    }

    // ---------- helpers ----------

    private async Task<JsonArray> Turn(params JsonObject[] messages)
    {
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0070-{++_n:D12}", ["wa_id"] = Client, ["contact_name"] = "عميل",
            ["messages"] = new JsonArray(messages),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray();
    }

    private JsonObject Text(string text) => new() { ["wamid"] = $"wamid.CW-{++_n}", ["type"] = "text", ["text"] = text };

    private async Task<int> Sync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ChatwootMirror>().SyncAsync(Ct);
    }

    private async Task<long> Conversation() =>
        await db.ScalarAsync<long>($"SELECT conversation_id FROM hazem.chatwoot_threads WHERE wa_id = '{Client}'");

    private async Task<HttpResponseMessage> Webhook(JsonObject payload, string? secret = ApiFactory.ChatwootWebhookSecret, long? at = null)
    {
        var body = payload.ToJsonString();
        var ts = (at ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chatwoot/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Chatwoot-Timestamp", ts);
        if (secret is not null)
        {
            request.Headers.Add("X-Chatwoot-Signature",
                "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{ts}.{body}"))));
        }

        return await _factory.CreateClient().SendAsync(request, Ct);
    }

    private static JsonObject StaffMessage(long id, long conversation, string text, long userId = 7, bool isPrivate = false) => new()
    {
        ["event"] = "message_created",
        ["id"] = id,
        ["content"] = text,
        ["message_type"] = "outgoing",
        ["private"] = isPrivate,
        ["sender"] = new JsonObject { ["id"] = userId, ["type"] = "user" },
        ["conversation"] = new JsonObject { ["id"] = conversation },
        ["attachments"] = new JsonArray(),
    };

    private async Task<string?> State() =>
        await db.ScalarAsync<string>($"SELECT c.state FROM hazem.conversations c JOIN hazem.persons p ON p.id = c.person_id WHERE p.wa_id = '{Client}'");

    // ---------- the client's words and Hazem's replies ----------

    [Fact]
    public async Task A_turn_is_copied_once_with_the_kind_of_a_file_never_its_place_or_name()
    {
        var actions = await Turn(Text("السلام عليكم"), new JsonObject
        {
            ["wamid"] = $"wamid.CW-{++_n}", ["type"] = "image", ["caption"] = "صورة البطاقة",
            ["media"] = new JsonObject { ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = "wa/2026/10/secret-key.jpg", ["filename"] = "بطاقة سارة.jpg" },
        });
        Assert.NotEmpty(actions);

        Assert.Equal(1, await Sync());
        Assert.Equal(0, await Sync()); // once
        var said = _factory.Chatwoot.Messages(await Conversation());
        Assert.Equal(["incoming", "incoming"], said.Take(2).Select(m => (string)m["message_type"]!));
        Assert.Equal("السلام عليكم", (string)said[0]["content"]!);
        var image = (string)said[1]["content"]!;
        Assert.Contains("صورة", image);
        Assert.Contains("صورة البطاقة", image); // the caption is the client's words
        Assert.DoesNotContain("secret-key", image);
        Assert.DoesNotContain("سارة.jpg", image);
        Assert.All(said.Skip(2), m => Assert.Equal("outgoing", (string)m["message_type"]!)); // Hazem's replies
        Assert.Equal(actions.Count, said.Count - 2);

        // Every call uses Hazem's token; the contact is the client's number.
        Assert.All(_factory.Chatwoot.Calls.Where(c => c.Path.StartsWith("/api/v1", StringComparison.Ordinal)),
            c => Assert.Equal("integration-test-chatwoot-bot-token", c.Token));
        var contact = _factory.Chatwoot.Calls.Single(c => c.Method == "POST" && c.Path.EndsWith("/contacts", StringComparison.Ordinal)).Body!;
        Assert.Equal(Client, (string)contact["identifier"]!);

        // The next turn goes to the same conversation.
        await Turn(Text("شكراً"));
        await Sync();
        Assert.Single(_factory.Chatwoot.Calls, c => c.Method == "POST" && c.Path.EndsWith("/conversations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Chatwoot_down_keeps_the_turn_for_later_and_the_clients_order()
    {
        await Turn(Text("الأولى"));
        _factory.Chatwoot.Down = true;
        Assert.Equal(0, await Sync());
        Assert.Equal("failed", await db.ScalarAsync<string>("SELECT state FROM hazem.chatwoot_turns"));
        Assert.True(await db.ScalarAsync<bool>("SELECT next_at > now() FROM hazem.chatwoot_turns"));

        _factory.Chatwoot.Down = false;
        await Turn(Text("التانية"));
        await db.ExecAsync("UPDATE hazem.chatwoot_turns SET next_at = now() - interval '1 second'");
        Assert.Equal(1, await Sync()); // the first; the second waited for it
        Assert.Equal(1, await Sync());
        var said = _factory.Chatwoot.Messages(await Conversation()).Where(m => (string)m["message_type"]! == "incoming")
            .Select(m => (string)m["content"]!).ToList();
        Assert.Equal(["الأولى", "التانية"], said);
    }

    [Fact]
    public async Task A_later_turn_waits_for_an_earlier_one_still_being_retried()
    {
        await Turn(Text("الأولى"));
        _factory.Chatwoot.Down = true;
        await Sync(); // the first fails and waits 30 s
        _factory.Chatwoot.Down = false;
        await Turn(Text("التانية"));

        Assert.Equal(0, await Sync()); // the second waits behind the first, in a later round too
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.chatwoot_turns WHERE state = 'done'"));

        await db.ExecAsync("UPDATE hazem.chatwoot_turns SET next_at = now() - interval '1 second'");
        Assert.Equal(1, await Sync());
        Assert.Equal(1, await Sync());
        Assert.Equal(["الأولى", "التانية"], _factory.Chatwoot.Messages(await Conversation())
            .Where(m => (string)m["message_type"]! == "incoming").Select(m => (string)m["content"]!));
    }

    [Fact]
    public async Task A_turn_copied_half_way_resumes_without_repeating_a_message()
    {
        await Turn(Text("واحد"), Text("اتنين"), Text("تلاتة"));
        _factory.Chatwoot.ArmMessageFailure(1); // the second message from now times out
        Assert.Equal(0, await Sync());
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT posted FROM hazem.chatwoot_turns"));

        await db.ExecAsync("UPDATE hazem.chatwoot_turns SET next_at = now() - interval '1 second'");
        Assert.Equal(1, await Sync());
        Assert.Equal(["واحد", "اتنين", "تلاتة"], _factory.Chatwoot.Messages(await Conversation())
            .Where(m => (string)m["message_type"]! == "incoming").Select(m => (string)m["content"]!));
    }

    // ---------- staff replies ----------

    [Fact]
    public async Task A_staff_reply_goes_out_once_through_n8n_and_hazem_goes_quiet()
    {
        await Turn(Text("السلام عليكم"));
        await Sync();
        var conversation = await Conversation();
        _factory.N8n.Requests.Clear();

        var r = await Webhook(StaffMessage(501, conversation, "أهلاً، معاك محمد من المكتب"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var sent = Assert.Single(_factory.N8n.Requests);
        var body = JsonNode.Parse(sent.Body)!;
        Assert.Equal(Client, (string)body["wa_id"]!);
        Assert.Equal("chatwoot:501", (string)body["idempotency_key"]!);
        Assert.Equal("أهلاً، معاك محمد من المكتب", (string)body["text"]!);
        Assert.Equal("human_handoff", await State());
        Assert.Empty(await Turn(Text("طيب"))); // Hazem stays quiet (B-12)

        // Delivered twice: sent once. Hazem's own copy and private notes: never sent.
        await Webhook(StaffMessage(501, conversation, "أهلاً، معاك محمد من المكتب"));
        await Webhook(StaffMessage(502, conversation, "نص حازم", userId: FakeChatwoot.SystemUser));
        await Webhook(StaffMessage(503, conversation, "ملاحظة داخلية", isPrivate: true));
        Assert.Single(_factory.N8n.Requests);
        Assert.Equal("sent", await db.ScalarAsync<string>("SELECT state FROM hazem.chatwoot_replies WHERE message_id = 501"));
        Assert.Equal(0, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.chatwoot_replies WHERE message_id IN (502, 503)"));
    }

    [Fact]
    public async Task A_refused_reply_is_said_in_a_private_note_and_hazem_keeps_answering()
    {
        await Turn(Text("السلام عليكم"));
        await Sync();
        var conversation = await Conversation();
        _factory.N8n.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":false,"error_code":"OUTSIDE_24H_WINDOW"}""", Encoding.UTF8, "application/json"),
        };

        await Webhook(StaffMessage(601, conversation, "رد متأخر"));
        Assert.Equal("failed", await db.ScalarAsync<string>("SELECT state FROM hazem.chatwoot_replies WHERE message_id = 601"));
        var note = _factory.Chatwoot.Messages(conversation).Last();
        Assert.True((bool)note["private"]!);
        Assert.Contains("24 ساعة", (string)note["content"]!);
        Assert.Equal("bot_active", await State());
    }

    [Fact]
    public async Task An_uncertain_send_is_said_as_uncertain_never_as_try_again()
    {
        await Turn(Text("السلام عليكم"));
        await Sync();
        var conversation = await Conversation();
        _factory.N8n.Responder = _ => throw new HttpRequestException("n8n timed out after sending");

        await Webhook(StaffMessage(651, conversation, "رد"));
        Assert.Equal("unknown", await db.ScalarAsync<string>("SELECT state FROM hazem.chatwoot_replies WHERE message_id = 651"));
        var note = (string)_factory.Chatwoot.Messages(conversation).Last()["content"]!;
        Assert.Contains("مش متأكدين", note);
        Assert.DoesNotContain("جرّب تاني", note);
    }

    [Fact]
    public async Task Only_a_signed_recent_delivery_is_heard()
    {
        await Turn(Text("السلام عليكم"));
        await Sync();
        var payload = StaffMessage(701, await Conversation(), "رد");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Webhook(payload, secret: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Webhook(payload, secret: "another secret")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Webhook(payload, at: DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds())).StatusCode);
        Assert.DoesNotContain(_factory.N8n.Requests, q => q.Body.Contains("chatwoot:701", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolving_the_conversation_hands_the_client_back_to_hazem()
    {
        await Turn(Text("السلام عليكم"));
        await Sync();
        var conversation = await Conversation();
        await Webhook(StaffMessage(801, conversation, "أهلاً"));
        Assert.Equal("human_handoff", await State());

        var r = await Webhook(new JsonObject { ["event"] = "conversation_status_changed", ["id"] = conversation, ["status"] = "resolved" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("bot_active", await State());
        Assert.Contains("رجعت لحازم", (string)_factory.Chatwoot.Messages(conversation).Last()["content"]!);
        Assert.NotEmpty(await Turn(Text("محمد أحمد علي حسن")));
    }

    // ---------- the panel's accounts ----------

    [Fact]
    public async Task Panel_accounts_follow_into_chatwoot_and_open_it_without_a_password()
    {
        using var scope = _factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<PanelAccounts>();
        var boss = (await accounts.CreateAsync(null, new PanelAccounts.NewAccount("boss", "المدير", "boss@example.test", Permissions.Manager, null), Password, Ct)).Value.User;
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ChatwootAccounts>().SyncAllAsync(Ct));
        // Everyone is an agent (the owner's choice, 1.10): only the system user administers Chatwoot.
        var bossRole = _factory.Chatwoot.Calls.Last(c => c.Path.EndsWith("/account_users", StringComparison.Ordinal)).Body!;
        Assert.Equal("agent", (string)bossRole["role"]!);
        var joined = _factory.Chatwoot.Calls.Last(c => c.Path.EndsWith("/inbox_members", StringComparison.Ordinal)).Body!;
        Assert.Equal(FakeChatwoot.Inbox, (long)joined["inbox_id"]!);
        Assert.True(await db.ScalarAsync<long>("SELECT coalesce(chatwoot_user_id, 0) FROM hazem.panel_users WHERE username = 'boss'") > 0);

        // The manager adds a staff member: an agent in Chatwoot; disabled: out of the account.
        var c = _factory.CreateClient();
        await Token(c);
        await c.PostAsJsonAsync("/api/panel/auth/login", new { username = "boss", password = Password }, Ct);
        await Token(c);
        var made = await c.PostAsJsonAsync("/api/panel/users", new { username = "yasmin", display_name = "ياسمين", email = "yasmin@example.test", role = "staff", staff_name = "ياسمين" }, Ct);
        Assert.Equal(HttpStatusCode.OK, made.StatusCode);
        var yasmin = (Guid)(await made.Content.ReadFromJsonAsync<JsonNode>(Ct))!["user"]!["id"]!;
        Assert.Equal("agent", (string)_factory.Chatwoot.Calls.Last(x => x.Path.EndsWith("/account_users", StringComparison.Ordinal)).Body!["role"]!);
        await c.PostAsync($"/api/panel/users/{yasmin}/disable", null, Ct);
        Assert.Equal("DELETE", _factory.Chatwoot.Calls.Last(x => x.Path.EndsWith("/account_users", StringComparison.Ordinal)).Method);
        Assert.All(_factory.Chatwoot.Calls.Where(x => x.Path.StartsWith("/platform", StringComparison.Ordinal)),
            x => Assert.Equal("integration-test-chatwoot-platform-token", x.Token));

        // Open Chatwoot: a one-time link on the public address.
        var open = await c.PostAsJsonAsync("/api/panel/chatwoot/open", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var link = (string)(await open.Content.ReadFromJsonAsync<JsonNode>(Ct))!["login_url"]!;
        Assert.StartsWith("http://127.0.0.1:3000/app/login?", link);
        Assert.Equal(boss.Id, (await accounts.FindByUsernameAsync("boss", Ct))!.Id);
    }

    [Fact]
    public async Task One_account_chatwoot_refuses_does_not_stop_the_others()
    {
        using var scope = _factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<PanelAccounts>();
        // A panel account with the system user's e-mail: Chatwoot would hand back that user. It is refused,
        // never linked, and the next account is still synced.
        await accounts.CreateAsync(null, new PanelAccounts.NewAccount("aaa", "أ", FakeChatwoot.SystemEmail, Permissions.Lawyer, null), Password, Ct);
        await accounts.CreateAsync(null, new PanelAccounts.NewAccount("bbb", "ب", "b@example.test", Permissions.Lawyer, null), Password, Ct);

        var failed = await scope.ServiceProvider.GetRequiredService<ChatwootAccounts>().SyncAllAsync(Ct);
        Assert.Single(failed);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_users WHERE username = 'aaa' AND chatwoot_user_id IS NOT NULL"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.panel_users WHERE username = 'bbb' AND chatwoot_user_id IS NOT NULL"));

        _factory.Chatwoot.Down = true;
        await db.ExecAsync("UPDATE hazem.panel_users SET chatwoot_user_id = NULL");
        Assert.Equal(2, (await scope.ServiceProvider.GetRequiredService<ChatwootAccounts>().SyncAllAsync(Ct)).Count);
    }

    [Fact]
    public async Task Without_its_tokens_the_link_is_off_and_nothing_is_called()
    {
        await using var bare = new ApiFactory(db);
        using var scope = bare.Services.CreateScope();
        await Turn(Text("السلام عليكم"));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ChatwootMirror>().SyncAsync(Ct));
        Assert.Empty(bare.Chatwoot.Calls);
    }

    private static async Task Token(HttpClient c)
    {
        var body = await c.GetFromJsonAsync<JsonNode>("/api/panel/auth/csrf", Ct);
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (string)body!["token"]!);
    }
}
