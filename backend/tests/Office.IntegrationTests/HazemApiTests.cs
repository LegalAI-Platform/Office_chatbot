using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Office.Core.Hazem;
using Office.Infrastructure.N8n;

namespace Office.IntegrationTests;

/// <summary>The real API and database (schema hazem as office_api); n8n is faked at the HTTP boundary.</summary>
[Collection(nameof(HazemApiTests))]
public sealed class HazemApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Recipient = "201000000051";
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync("TRUNCATE hazem.turns, hazem.send_results");
        await db.SetEchoAsync(false, "");
        _factory = new ApiFactory(db);
        _n8n = _factory.CreateN8nClient();
    }

    public async ValueTask DisposeAsync()
    {
        _n8n.Dispose();
        await _factory.DisposeAsync();
    }

    private static JsonObject Turn(string turnId, string waId = Recipient, string text = "عايز أحجز استشارة") => new()
    {
        ["turn_id"] = turnId,
        ["wa_id"] = waId,
        ["contact_name"] = "Test Client",
        ["messages"] = new JsonArray(new JsonObject
        {
            ["wamid"] = "wamid." + turnId,
            ["type"] = "text",
            ["text"] = text,
            ["sent_at"] = "2026-10-20T10:15:00+03:00",
        }),
    };

    private async Task<(HttpStatusCode Status, JsonNode? Body)> PostTurn(HttpClient client, JsonObject turn)
    {
        var r = await client.PostAsJsonAsync("/api/hazem/turns", turn, Ct);
        var text = await r.Content.ReadAsStringAsync(Ct);
        return (r.StatusCode, text.Length > 0 ? JsonNode.Parse(text) : null);
    }

    // ---------- authentication ----------

    [Fact]
    public async Task Turn_without_the_token_is_401_and_stores_nothing()
    {
        using var anonymous = _factory.CreateClient();
        var (status, _) = await PostTurn(anonymous, Turn("wa:noauth"));
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.turns"));
    }

    [Fact]
    public async Task Turn_with_a_wrong_token_is_401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(N8nWhatsAppGateway.TokenHeader, ApiFactory.N8nToken); // the other direction's secret
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostTurn(client, Turn("wa:wrongauth"))).Status);
    }

    [Fact]
    public async Task Send_results_without_the_token_is_401()
    {
        using var anonymous = _factory.CreateClient();
        var r = await anonymous.PostAsJsonAsync("/api/hazem/send-results", new { results = Array.Empty<object>() }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ---------- turns ----------

    [Fact]
    public async Task Invalid_turn_is_400_with_the_field_name()
    {
        var (status, body) = await PostTurn(_n8n, Turn("wa:bad", waId: "12ab"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.NotNull(body?["errors"]?["wa_id"]);
    }

    [Fact]
    public async Task Turn_is_stored_and_answers_nothing_while_echo_is_off()
    {
        var (status, body) = await PostTurn(_n8n, Turn("wa:quiet"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("processed", (string?)body!["status"]);
        Assert.Empty(body["actions"]!.AsArray());
        Assert.Equal("none|عايز أحجز استشارة", await db.ScalarAsync<string>(
            "SELECT responder || '|' || (messages->0->>'text') FROM hazem.turns WHERE turn_id = 'wa:quiet'"));
    }

    [Fact]
    public async Task Echo_answers_a_listed_number_and_a_replay_returns_the_same_action()
    {
        await db.SetEchoAsync(true, Recipient);
        var (_, first) = await PostTurn(_n8n, Turn("wa:echo"));
        var action = Assert.Single(first!["actions"]!.AsArray())!;
        Assert.Equal("send_text", (string?)action["type"]);
        Assert.Equal(TurnResponder.EchoText, (string?)action["text"]);
        Assert.Equal("hazem:wa:echo:0", (string?)action["idempotency_key"]);
        Assert.Equal("wamid.wa:echo", (string?)action["context_wamid"]);

        await db.SetEchoAsync(false, "");
        var (status, second) = await PostTurn(_n8n, Turn("wa:echo"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("duplicate", (string?)second!["status"]);
        Assert.Equal(first["actions"]!.ToJsonString(), second["actions"]!.ToJsonString());
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.turns WHERE turn_id = 'wa:echo'"));
    }

    [Fact]
    public async Task Echo_never_answers_a_number_that_is_not_listed()
    {
        await db.SetEchoAsync(true, Recipient);
        var (_, body) = await PostTurn(_n8n, Turn("wa:stranger", waId: "201000000059"));
        Assert.Empty(body!["actions"]!.AsArray());
    }

    [Fact]
    public async Task Two_identical_turns_at_the_same_time_store_one_row()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostTurn(_n8n, Turn("wa:race"))));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Single(results, r => (string?)r.Body!["status"] == "processed");
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.turns WHERE turn_id = 'wa:race'"));
    }

    // ---------- TASK 03: grouped turns with media ----------

    private static JsonObject MediaTurn(string turnId) => new()
    {
        ["turn_id"] = turnId,
        ["wa_id"] = Recipient,
        ["contact_name"] = "Test Client",
        ["messages"] = new JsonArray(
            new JsonObject { ["wamid"] = "wamid.m1", ["type"] = "text", ["text"] = "دي بطاقتي", ["sent_at"] = "2026-10-20T10:15:00+03:00", ["context_wamid"] = null },
            new JsonObject
            {
                ["wamid"] = "wamid.m2", ["type"] = "image", ["caption"] = "CAPTION-SECRET", ["sent_at"] = "2026-10-20T10:15:02+03:00",
                ["media"] = new JsonObject
                {
                    ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = "wa/2026/10/0b6c.jpg",
                    ["mime_type"] = "image/jpeg", ["size_bytes"] = 1234, ["sha256"] = "ab",
                },
            },
            new JsonObject
            {
                ["wamid"] = "wamid.m3", ["type"] = "document", ["caption"] = null, ["sent_at"] = "2026-10-20T10:15:03+03:00",
                ["media"] = new JsonObject { ["status"] = "too_large", ["mime_type"] = "application/pdf", ["size_bytes"] = 73400320, ["filename"] = "FILENAME-SECRET.pdf" },
            },
            new JsonObject
            {
                ["wamid"] = "wamid.m4", ["type"] = "audio", ["sent_at"] = "2026-10-20T10:15:04+03:00",
                ["media"] = new JsonObject { ["status"] = "stored", ["bucket"] = "office-media", ["key"] = "wa/2026/10/1a2b.ogg", ["mime_type"] = "audio/ogg", ["voice"] = true },
                ["transcript"] = new JsonObject { ["status"] = "done", ["text"] = "TRANSCRIPT-SECRET عايز أحجز", ["language"] = "ar" },
            }),
    };

    [Fact]
    public async Task Grouped_turn_with_media_is_stored_and_echoed_from_the_transcript()
    {
        await db.SetEchoAsync(true, Recipient);
        var (status, body) = await PostTurn(_n8n, MediaTurn("wt:00000000-0000-0000-0000-000000000001"));
        Assert.Equal(HttpStatusCode.OK, status);
        var action = Assert.Single(body!["actions"]!.AsArray())!;
        Assert.StartsWith(TurnResponder.EchoVoicePrefix + "TRANSCRIPT-SECRET", (string?)action["text"]);
        Assert.Equal("4|office-sensitive|too_large", await db.ScalarAsync<string>("""
            SELECT jsonb_array_length(messages) || '|' || (messages->1->'media'->>'bucket') || '|' || (messages->2->'media'->>'status')
              FROM hazem.turns WHERE turn_id = 'wt:00000000-0000-0000-0000-000000000001'
            """));
    }

    [Fact]
    public async Task Media_turn_with_a_url_in_a_reference_is_400()
    {
        var turn = MediaTurn("wt:00000000-0000-0000-0000-000000000002");
        turn["messages"]![1]!["media"]!["key"] = "https://garage:3900/office-sensitive/x.jpg";
        var (status, body) = await PostTurn(_n8n, turn);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.NotNull(body?["errors"]?["messages[1].media.key"]);
    }

    [Fact]
    public async Task Logs_never_contain_captions_transcripts_file_names_or_text()
    {
        await db.SetEchoAsync(true, Recipient);
        await PostTurn(_n8n, MediaTurn("wt:00000000-0000-0000-0000-000000000003"));
        var all = string.Join("\n", _factory.Logs.Lines);
        Assert.Contains("wt:00000000-0000-0000-0000-000000000003", all); // the turn was logged
        Assert.Contains("text,image,document,audio", all);              // types only
        foreach (var secret in new[] { "CAPTION-SECRET", "TRANSCRIPT-SECRET", "FILENAME-SECRET", "دي بطاقتي", Recipient })
        {
            Assert.DoesNotContain(secret, all);
        }
    }

    // ---------- send results ----------

    [Fact]
    public async Task Send_results_are_recorded_once_per_key_and_updated()
    {
        async Task Report(string status, bool ok) => Assert.Equal(HttpStatusCode.OK, (await _n8n.PostAsJsonAsync("/api/hazem/send-results", new JsonObject
        {
            ["turn_id"] = "wa:results",
            ["results"] = new JsonArray(new JsonObject
            {
                ["idempotency_key"] = "hazem:wa:results:0",
                ["ok"] = ok,
                ["wamid"] = "wamid.OUT-1",
                ["status"] = status,
                ["error_code"] = ok ? null : "RATE_LIMIT",
            }),
        }, Ct)).StatusCode);

        await Report("failed", ok: false);
        await Report("sent", ok: true);
        Assert.Equal("1|sent|true|n8n|wa:results", await db.ScalarAsync<string>("""
            SELECT count(*) OVER () || '|' || status || '|' || ok || '|' || reported_by || '|' || turn_id
              FROM hazem.send_results WHERE idempotency_key = 'hazem:wa:results:0'
            """));
    }

    // ---------- API -> n8n ----------

    [Fact]
    public async Task Outbound_send_calls_n8n_with_its_own_token_and_records_the_result()
    {
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<OutboundService>().SendAsync(
            new OutboundMessage(TurnAction.SendText, Recipient, "test:outbound:1", "integration-test", Text: "hello"), Ct);

        Assert.True(result.Ok);
        var call = Assert.Single(_factory.N8n.Requests);
        Assert.Equal(ApiFactory.N8nUrl, call.Url);
        Assert.Equal(ApiFactory.N8nToken, call.Token);
        var sent = JsonNode.Parse(call.Body)!;
        Assert.Equal("send_text", (string?)sent["type"]);
        Assert.Equal("test:outbound:1", (string?)sent["idempotency_key"]);
        Assert.Equal("true|api|" + result.Wamid, await db.ScalarAsync<string>(
            "SELECT ok || '|' || reported_by || '|' || wamid FROM hazem.send_results WHERE idempotency_key = 'test:outbound:1'"));
    }

    [Fact]
    public async Task Outbound_send_refused_by_n8n_is_recorded_as_failed()
    {
        _factory.N8n.Responder = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<OutboundService>().SendAsync(
            new OutboundMessage(TurnAction.SendText, Recipient, "test:outbound:403", "integration-test", Text: "hello"), Ct);

        Assert.False(result.Ok);
        Assert.Equal("N8N_HTTP_403", result.ErrorCode);
        Assert.Single(_factory.N8n.Requests); // 403 is not retried
    }

    // ---------- database boundaries and health ----------

    [Fact]
    public async Task Office_api_has_no_access_to_the_whatsapp_tables()
    {
        await using var c = new NpgsqlConnection(db.ApiConnectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM app.wa_inbound_messages", c);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync(Ct));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Office_app_has_no_access_to_the_api_tables()
    {
        await using var c = new NpgsqlConnection(db.AppConnectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM hazem.turns", c);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync(Ct));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Ready_check_is_healthy_when_the_schema_exists()
    {
        using var client = _factory.CreateClient();
        Assert.Equal("Healthy", await client.GetStringAsync("/health/ready", Ct));
    }
}
