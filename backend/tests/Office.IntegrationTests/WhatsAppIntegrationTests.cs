using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Office.Core.WhatsApp;

namespace Office.IntegrationTests;

/// <summary>
/// End-to-end through the real HTTP endpoint and database. The fixtures in
/// tests/fixtures/whatsapp are change values (plus "field"), wrapped here in Meta's envelope.
/// </summary>
[Collection(nameof(WhatsAppIntegrationTests))]
public sealed class WhatsAppIntegrationTests(TestDatabase db) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await db.ResetAsync();
        _factory = new ApiFactory(db);
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ---------- webhook verification ----------

    [Fact]
    public async Task Get_with_the_right_verify_token_returns_the_challenge()
    {
        var r = await _client.GetAsync($"/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={ApiFactory.VerifyToken}&hub.challenge=12345", Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("12345", await r.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Get_with_a_wrong_verify_token_is_forbidden()
    {
        var r = await _client.GetAsync("/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=nope&hub.challenge=1", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Post_with_a_bad_signature_is_rejected_and_stores_nothing()
    {
        var body = Envelope("01_text.json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", WebhookSignature.Compute(body, "wrong-secret"));

        var r = await _client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_webhook_events"));
    }

    [Fact]
    public async Task Post_without_a_signature_is_rejected()
    {
        var r = await _client.PostAsync("/webhooks/whatsapp", new ByteArrayContent(Envelope("01_text.json")), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ---------- inbound events ----------

    [Fact]
    public async Task Text_event_creates_a_pending_message_and_opens_the_window()
    {
        await PostAndProcess("01_text.json");

        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_webhook_events WHERE processing_status = 'processed'"));
        Assert.Equal("text/pending/Test Client One", await db.ScalarAsync<string>(
            "SELECT message_type || '/' || processing_status || '/' || contact_name FROM app.wa_inbound_messages WHERE wamid = 'wamid.TEST-IN-TEXT-1'"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_contact_windows WHERE wa_id = '201000000001'"));
    }

    [Fact]
    public async Task Same_payload_twice_is_stored_once()
    {
        await PostAndProcess("01_text.json");
        await PostAndProcess("01_text.json");

        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_webhook_events"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_inbound_messages"));
    }

    [Fact]
    public async Task Same_wamid_in_another_event_is_not_duplicated()
    {
        await PostAndProcess("01_text.json");
        await PostAndProcess("02_text_same_wamid_other_event.json");

        Assert.Equal(2L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_webhook_events"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_inbound_messages WHERE wamid = 'wamid.TEST-IN-TEXT-1'"));
    }

    [Fact]
    public async Task Media_messages_are_stored_by_type_without_downloading()
    {
        foreach (var f in new[] { "03_audio.json", "04_image.json", "05_document.json" })
        {
            await PostAndProcess(f);
        }

        Assert.Equal("audio,document,image", await db.ScalarAsync<string>(
            "SELECT string_agg(message_type, ',' ORDER BY message_type) FROM app.wa_inbound_messages"));
        Assert.Equal("MEDIA-AUDIO-1", await db.ScalarAsync<string>(
            "SELECT raw->'audio'->>'id' FROM app.wa_inbound_messages WHERE wamid = 'wamid.TEST-IN-AUDIO-1'"));
        Assert.Empty(_factory.Graph.Requests); // nothing fetched from Meta
    }

    [Fact]
    public async Task Window_never_moves_backwards()
    {
        await PostAndProcess("05_document.json"); // newest
        await PostAndProcess("01_text.json");     // older, arrives late
        Assert.Equal("wamid.TEST-IN-DOC-1", await db.ScalarAsync<string>(
            "SELECT last_inbound_wamid FROM app.wa_contact_windows WHERE wa_id = '201000000001'"));
    }

    [Fact]
    public async Task Click_to_whatsapp_referral_is_saved()
    {
        await PostAndProcess("06_referral.json");
        Assert.Equal("AD-123", await db.ScalarAsync<string>(
            "SELECT referral->>'source_id' FROM app.wa_inbound_messages WHERE wamid = 'wamid.TEST-IN-REF-1'"));
    }

    [Fact]
    public async Task Group_message_is_ignored_and_opens_no_window()
    {
        await PostAndProcess("07_group.json");
        Assert.Equal("ignored_group/True", await db.ScalarAsync<string>(
            "SELECT processing_status || '/' || initcap(is_group::text) FROM app.wa_inbound_messages WHERE wamid = 'wamid.TEST-IN-GROUP-1'"));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_contact_windows WHERE wa_id = '201000000003'"));
    }

    [Fact]
    public async Task Statuses_only_move_forward_and_failed_keeps_the_error()
    {
        await OpenWindow(ApiFactory.TestRecipient);
        await db.ExecAsync("""
            INSERT INTO app.wa_outbound_messages (id, idempotency_key, wa_id, kind, request_payload, status, attempts, requested_by, created_at, updated_at, wamid)
            VALUES (gen_random_uuid(), 'k1', '201000000001', 'text', '{}', 'sent', 1, 'test', now(), now(), 'wamid.TEST-OUT-1'),
                   (gen_random_uuid(), 'k2', '201000000001', 'text', '{}', 'sent', 1, 'test', now(), now(), 'wamid.TEST-OUT-2')
            """);

        await PostAndProcess("10_status_sent.json");
        await PostAndProcess("11_status_read.json");
        await PostAndProcess("12_status_delivered_late.json");
        await PostAndProcess("13_status_failed_other.json");

        Assert.Equal("read", await db.ScalarAsync<string>("SELECT status FROM app.wa_outbound_messages WHERE wamid = 'wamid.TEST-OUT-1'"));
        Assert.Equal(3L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_message_statuses WHERE wamid = 'wamid.TEST-OUT-1'"));
        Assert.Equal("failed/131026", await db.ScalarAsync<string>(
            "SELECT status || '/' || (last_error->0->>'code') FROM app.wa_outbound_messages WHERE wamid = 'wamid.TEST-OUT-2'"));
    }

    [Fact]
    public async Task Template_status_update_is_recorded()
    {
        await PostAndProcess("20_template_approved.json");
        Assert.Equal("APPROVED", await db.ScalarAsync<string>("SELECT status FROM app.wa_templates WHERE name = 'hello_world' AND language = 'en_US'"));
    }

    [Fact]
    public async Task Webhook_answers_in_under_one_second()
    {
        var body = Envelope("01_text.json");
        await Post(body); // warm-up (JIT, connections)
        await db.ResetAsync();

        var sw = Stopwatch.StartNew();
        var r = await Post(body);
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"webhook took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM app.wa_inbound_messages")); // processing happens later
    }

    // ---------- sending ----------

    [Fact]
    public async Task Text_25_hours_after_the_last_message_is_blocked_without_calling_meta()
    {
        await db.ExecAsync("INSERT INTO app.wa_contact_windows (wa_id, last_inbound_at, updated_at) VALUES ('201000000001', now() - interval '25 hours', now())");

        var result = await Sender().SendTextAsync(ApiFactory.TestRecipient, "hi", "t-blocked", "test", null, Ct);

        Assert.Equal(SendErrorCodes.OutsideWindow, result.ErrorCode);
        Assert.Equal(OutboundStatuses.BlockedWindow, result.Status);
        Assert.Empty(_factory.Graph.Requests);
    }

    [Fact]
    public async Task Same_idempotency_key_sends_only_once()
    {
        await OpenWindow(ApiFactory.TestRecipient);

        var first = await Sender().SendTextAsync(ApiFactory.TestRecipient, "hi", "t-once", "test", null, Ct);
        var second = await Sender().SendTextAsync(ApiFactory.TestRecipient, "hi", "t-once", "test", null, Ct);

        Assert.True(first.Ok);
        Assert.True(second.Duplicate);
        Assert.Equal(first.Wamid, second.Wamid);
        Assert.Single(_factory.Graph.Requests);
        Assert.Contains($"/v26.0/{ApiFactory.PhoneNumberId}/messages", _factory.Graph.Requests.Single().Path);
    }

    [Fact]
    public async Task Template_that_is_not_approved_is_refused_without_calling_meta()
    {
        var result = await Sender().SendTemplateAsync(ApiFactory.TestRecipient, "reminder_day_before", "ar", null, "t-tpl", "test", Ct);

        Assert.Equal(SendErrorCodes.TemplateNotApproved, result.ErrorCode);
        Assert.Empty(_factory.Graph.Requests);
    }

    [Fact]
    public async Task Approved_template_is_sent_outside_the_window()
    {
        await PostAndProcess("20_template_approved.json");
        var result = await Sender().SendTemplateAsync(ApiFactory.TestRecipient, "hello_world", "en_US", null, "t-tpl-ok", "test", Ct);
        Assert.True(result.Ok);
        Assert.Single(_factory.Graph.Requests);
    }

    [Fact]
    public async Task Meta_error_190_maps_to_token_invalid_and_alerts_the_operator()
    {
        await OpenWindow(ApiFactory.TestRecipient);
        _factory.Graph.Responder = _ => FakeGraphHandler.MetaError(190, "Invalid OAuth access token");

        var result = await Sender().SendTextAsync(ApiFactory.TestRecipient, "hi", "t-190", "test", null, Ct);

        Assert.Equal(SendErrorCodes.TokenInvalid, result.ErrorCode);
        Assert.Contains(_factory.Notifier.Messages, m => m.Contains("190", StringComparison.Ordinal));
        Assert.Equal("failed", await db.ScalarAsync<string>("SELECT status FROM app.wa_outbound_messages WHERE idempotency_key = 't-190'"));
    }

    [Fact]
    public async Task Echo_test_replies_once_to_test_recipients_only()
    {
        await PostAndProcess("01_text.json"); // from 201000000001 (a test recipient)
        var echo = Scope().GetRequiredService<EchoResponder>();

        // EchoTest is off by default: nothing is sent.
        Assert.Equal(0, await echo.RunAsync(Ct));
        Assert.Empty(_factory.Graph.Requests);
    }

    // ---------- permissions ----------

    [Fact]
    public async Task Api_database_user_cannot_change_the_schema()
    {
        await using var c = new NpgsqlConnection(db.AppConnectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("CREATE TABLE app.__probe(x int)", c);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync(Ct));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    // ---------- helpers ----------

    private MessageSender Sender() => Scope().GetRequiredService<MessageSender>();

    private IServiceProvider Scope() => _factory.Services.CreateScope().ServiceProvider;

    private Task OpenWindow(string waId) =>
        db.ExecAsync($"INSERT INTO app.wa_contact_windows (wa_id, last_inbound_at, updated_at) VALUES ('{waId}', now(), now()) ON CONFLICT (wa_id) DO UPDATE SET last_inbound_at = now()");

    private async Task PostAndProcess(string fixture)
    {
        var r = await Post(Envelope(fixture));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // Hangfire's server is off in tests: run the queued work directly, as the job would.
        var processor = Scope().GetRequiredService<EventProcessor>();
        await using var c = new NpgsqlConnection(db.AppConnectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("SELECT id FROM app.wa_webhook_events WHERE processing_status = 'received' ORDER BY received_at", c);
        var ids = new List<Guid>();
        await using (var reader = await cmd.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        foreach (var id in ids)
        {
            await processor.ProcessAsync(id, Ct);
        }
    }

    private Task<HttpResponseMessage> Post(byte[] body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", WebhookSignature.Compute(body, ApiFactory.AppSecret));
        return _client.SendAsync(request, Ct);
    }

    private static byte[] Envelope(string fixture)
    {
        var value = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "whatsapp", fixture)))!.AsObject();
        var field = value["field"]?.GetValue<string>() ?? "messages";
        value.Remove("field");
        var envelope = new JsonObject
        {
            ["object"] = "whatsapp_business_account",
            ["entry"] = new JsonArray(new JsonObject
            {
                ["id"] = "800000000000001",
                ["changes"] = new JsonArray(new JsonObject { ["field"] = field, ["value"] = value }),
            }),
        };
        return Encoding.UTF8.GetBytes(envelope.ToJsonString());
    }
}
