using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Office.Infrastructure.Ai;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 04 through the real API and database: responder hazem, OpenAI faked at the HTTP boundary.
/// Same collection as HazemApiTests: they share the settings table, so they never run at once.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class HazemConversationApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Client = "201000000071";
    private const string Welcome = "أهلًا بحضرتك في مكتب الأستاذ سعد فتحي سعد للمحاماة، معاك حازم. ممكن اسم حضرتك؟"; // v1.29: no «رباعي»
    private const string Offer = "الأستاذ يقدر يشوف الموضوع مع حضرتك في استشارة: بالتليفون بـ500 أو في المكتب بـ1,000.";
    private const string AskTopic = "ولو تحب، قولّي باختصار الموضوع عن إيه عشان الأستاذ يبقى عارفه.";
    private ApiFactory _factory = null!;
    private HttpClient _n8n = null!;
    private int _n;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync("""
            TRUNCATE hazem.turns, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
            TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users; -- they point at hazem.staff
            DELETE FROM hazem.staff;
            DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
            """);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", Client);
        await db.SetAsync("hazem.open_to_all", "false");
        _factory = new ApiFactory(db);
        _n8n = _factory.CreateN8nClient();
    }

    public async ValueTask DisposeAsync()
    {
        _n8n.Dispose();
        await _factory.DisposeAsync();
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.recipients", "");
    }

    private async Task<JsonNode> Post(JsonArray messages, string waId = Client)
    {
        var r = await _n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
        {
            ["turn_id"] = $"wt:00000000-0000-0000-0004-{++_n:D12}",
            ["wa_id"] = waId,
            ["contact_name"] = "CONTACT-SECRET",
            ["messages"] = messages,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
    }

    private Task<JsonNode> Say(string text, string waId = Client) => Post(new JsonArray(new JsonObject
    {
        ["wamid"] = $"wamid.T04-{_n + 1}", ["type"] = "text", ["text"] = text, ["sent_at"] = "2026-10-22T12:00:00+03:00", ["context_wamid"] = null,
    }), waId);

    private static List<string?> Texts(JsonNode response) => response["actions"]!.AsArray().Select(a => (string?)a!["text"]).ToList();

    [Fact]
    public async Task Booking_request_then_the_name_gets_the_offer_without_asking_again()
    {
        // v1.21 (B-01.6): the salam is returned first, then the welcome.
        Assert.Equal(["وعليكم السلام ورحمة الله وبركاته", Welcome], Texts(await Say("السلام عليكم عايز أحجز استشارة")));
        Assert.Equal([Offer, AskTopic], Texts(await Say("محمد أحمد علي حسن"))); // B-25: the topic is still unknown

        Assert.Equal("محمد أحمد علي حسن|true|bot_active|offer_sent|new_client|", await db.ScalarAsync<string>($"""
            SELECT p.name_normalized || '|' || p.full_name_provided || '|' || c.state || '|' || i.state || '|' || i.client_type || '|' || COALESCE(i.pending_intent, '')
              FROM hazem.persons p JOIN hazem.conversations c ON c.person_id = p.id JOIN hazem.intakes i ON i.person_id = p.id
             WHERE p.wa_id = '{Client}'
            """));
        Assert.Equal("greet_salam:1,welcome_ask_name:2,consultation_offer:2,ask_topic:1", await db.ScalarAsync<string>(
            "SELECT string_agg(text_key || ':' || text_version, ',' ORDER BY created_at, idempotency_key) FROM hazem.turn_replies"));
    }

    [Fact]
    public async Task Legal_question_gets_b20_and_the_offer()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        // v1.30: the services list came after the name (not the offer), so the topic question follows the offer here
        Assert.Equal(["الأستاذ هو اللي هيجاوب حضرتك على ده في الاستشارة.", Offer, "ولو تحب، قولّي باختصار الموضوع عن إيه عشان الأستاذ يبقى عارفه."],
            Texts(await Say("ينفع أطلق؟")));
    }

    [Fact]
    public async Task Ai_down_still_answers()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        _factory.OpenAi.Down = true;
        Assert.Equal(["معلش مش واضحلي المطلوب. ممكن تكتبلي حضرتك محتاج إيه؟"], Texts(await Say("عايز أحجز"))); // «unclear» (0007)
        Assert.Equal("other|error", await db.ScalarAsync<string>(
            "SELECT intent || '|' || fallback_reason FROM hazem.classifications ORDER BY created_at DESC LIMIT 1"));
    }

    [Fact]
    public async Task Human_request_records_a_task_and_sends_nothing_while_its_text_is_inactive()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        Assert.Equal(["تمام، هبلّغ المكتب إن حضرتك عايز تكلم حد."], Texts(await Say("عايز أكلم موظف"))); // approved (0007)
        Assert.Equal("human_requested|open", await db.ScalarAsync<string>("SELECT type || '|' || state FROM hazem.staff_tasks"));

        await db.ExecAsync("UPDATE hazem.approved_texts SET active = false WHERE key = 'human_request'");
        try
        {
            Assert.Empty(Texts(await Say("عايز أكلم موظف تاني")));
        }
        finally
        {
            await db.ExecAsync("UPDATE hazem.approved_texts SET active = true WHERE key = 'human_request'");
        }
    }

    [Fact]
    public async Task T09_a_photo_reaches_no_ai_and_the_ai_never_sees_numbers_or_file_fields()
    {
        await Post(new JsonArray(new JsonObject
        {
            ["wamid"] = "wamid.T04-img", ["type"] = "image", ["caption"] = null, ["sent_at"] = "2026-10-22T12:00:00+03:00",
            ["media"] = new JsonObject
            {
                ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = "wa/2026/10/abc.jpg",
                ["mime_type"] = "image/jpeg", ["size_bytes"] = 10, ["sha256"] = "ab", ["filename"] = "FILENAME-SECRET.jpg",
            },
        }));
        Assert.Empty(_factory.OpenAi.Requests);

        await Post(new JsonArray(
            new JsonObject { ["wamid"] = "wamid.T04-t", ["type"] = "text", ["text"] = "عايز أحجز رقمي 01012345678", ["sent_at"] = "2026-10-22T12:00:00+03:00" },
            new JsonObject
            {
                ["wamid"] = "wamid.T04-d", ["type"] = "document", ["caption"] = "العقد", ["sent_at"] = "2026-10-22T12:00:01+03:00",
                ["media"] = new JsonObject { ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = "wa/2026/10/KEY-SECRET.pdf", ["filename"] = "FILENAME-SECRET.pdf", ["mime_type"] = "application/pdf" },
            }));
        var (auth, seen) = Assert.Single(_factory.OpenAi.Requests);
        Assert.Equal("Bearer " + ApiFactory.OpenAiKey, auth);
        Assert.Equal("عايز أحجز رقمي [رقم]\nالعقد", seen);
        foreach (var leak in new[] { "0101", "KEY-SECRET", "FILENAME-SECRET", "office-sensitive", "application/pdf", "wa/2026" })
        {
            Assert.DoesNotContain(leak, seen);
        }
    }

    [Fact]
    public async Task Classification_log_and_api_logs_hold_no_client_text_and_no_name()
    {
        await Say("السلام عليكم TEXT-SECRET");
        await Say("محمد أحمد علي حسن");
        var rows = await db.ScalarAsync<string>("SELECT string_agg(row_to_json(c)::text, '\n') FROM hazem.classifications c");
        Assert.Contains("\"full_name_present\":true", rows);
        Assert.Contains($"\"prompt_version\":\"{OpenAiIntentClassifier.PromptVersion}\"", rows);
        var logs = string.Join("\n", _factory.Logs.Lines);
        foreach (var secret in new[] { "TEXT-SECRET", "محمد", "CONTACT-SECRET", Client, ApiFactory.OpenAiKey })
        {
            Assert.DoesNotContain(secret, rows);
            Assert.DoesNotContain(secret, logs);
        }
    }

    [Fact]
    public async Task A_text_that_cannot_be_sent_is_logged_by_key_and_reason()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        await db.ExecAsync("UPDATE hazem.approved_texts SET active = false WHERE key = 'thanks'");
        try
        {
            Assert.Empty(Texts(await Say("شكرا")));
        }
        finally
        {
            await db.ExecAsync("UPDATE hazem.approved_texts SET active = true WHERE key = 'thanks'");
        }

        var logs = string.Join("\n", _factory.Logs.Lines);
        Assert.Contains("text thanks not sent: inactive_or_missing", logs);
        Assert.DoesNotContain("شكرا", logs);
    }

    [Fact]
    public async Task A_number_outside_hazem_recipients_gets_no_answer()
    {
        Assert.Empty(Texts(await Say("السلام عليكم", waId: "201000000079")));
        Assert.Empty(_factory.OpenAi.Requests);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.persons"));
    }

    [Fact]
    public async Task Replayed_turn_returns_the_same_actions_and_changes_nothing_twice()
    {
        var turn = new JsonObject
        {
            ["turn_id"] = "wt:00000000-0000-0000-0004-999999999999", ["wa_id"] = Client,
            ["messages"] = new JsonArray(new JsonObject { ["wamid"] = "wamid.T04-r", ["type"] = "text", ["text"] = "السلام عليكم" }),
        };
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _n8n.PostAsJsonAsync("/api/hazem/turns", turn, Ct)));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        // one turn, one person, its two replies (v1.21: the salam returned, then the welcome) once, one name ask
        Assert.Equal("1|1|2|1", await db.ScalarAsync<string>("""
            SELECT (SELECT count(*) FROM hazem.turns) || '|' || (SELECT count(*) FROM hazem.persons) || '|' ||
                   (SELECT count(*) FROM hazem.turn_replies) || '|' || (SELECT name_asks FROM hazem.intakes)
            """));
    }

    // ---------- migration 0025: «انسى الرقم ده» (test environment) ----------

    [Fact]
    public async Task Forget_wa_id_deletes_every_row_of_the_number_and_hazem_starts_again()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        await Say("عايز أحجز استشارة");
        var counts = $"""
            SELECT (SELECT count(*) FROM hazem.persons WHERE wa_id = '{Client}') || '|' ||
                   (SELECT count(*) FROM hazem.turns WHERE wa_id = '{Client}') || '|' ||
                   (SELECT count(*) FROM hazem.turn_replies r JOIN hazem.turns t USING (turn_id) WHERE t.wa_id = '{Client}') || '|' ||
                   (SELECT count(*) FROM hazem.intakes i JOIN hazem.persons p ON p.id = i.person_id WHERE p.wa_id = '{Client}')
            """;
        Assert.Equal("1|3|", (await db.ScalarAsync<string>(counts))![..4]);

        var result = await db.ScalarAsync<string>($"SELECT hazem.forget_wa_id('{Client}')::text");
        Assert.Contains("\"persons\": 1", result, StringComparison.Ordinal);
        Assert.Contains("\"calendar_events\": []", result, StringComparison.Ordinal);
        Assert.Equal("0|0|0|0", await db.ScalarAsync<string>(counts));

        // From the start again: the welcome and the name, as a new number.
        Assert.Equal(["وعليكم السلام ورحمة الله وبركاته", Welcome], Texts(await Say("السلام عليكم")));
    }

    [Fact]
    public async Task Forget_wa_id_refuses_anything_but_digits()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.ScalarAsync<string>("SELECT hazem.forget_wa_id('201%')::text"));
        Assert.Contains("digits only", error.Message, StringComparison.Ordinal);
    }

    // ---------- migration 0005 ----------

    [Fact]
    public async Task Every_active_text_is_word_for_word_in_decisions()
    {
        // v1.12 for the texts of 1.4 (booking_confirmed is still active, not sent), v1.16 for B-11 (TASK 11) and B-18 (TASK 12),
        // v1.21 (it holds v1.20's B-11.1 closing text too) for B-01.6's greetings.
        var decisions = await File.ReadAllTextAsync(RepoFile("docs", "decisions", "DECISIONS-v1.12.md"), Ct)
                        + await File.ReadAllTextAsync(RepoFile("docs", "decisions", "DECISIONS-v1.16.md"), Ct)
                        + await File.ReadAllTextAsync(RepoFile("docs", "decisions", "DECISIONS-v1.22.md"), Ct)
                        + await File.ReadAllTextAsync(RepoFile("docs", "decisions", "DECISIONS-v1.25.md"), Ct) // B-08 / B-23 / B-18 of 1.25
                        + await File.ReadAllTextAsync(RepoFile("docs", "decisions", "DECISIONS-v1.28.md"), Ct) // B-23's case offer of 1.28
                        + await File.ReadAllTextAsync(RepoFile("docs", "decisions", "DECISIONS-v1.29.md"), Ct) // B-01 / B-02 of 1.29
                        + await File.ReadAllTextAsync(RepoFile("docs", "decisions", "DECISIONS-v1.30.md"), Ct) // the services list of 1.30
                        + await File.ReadAllTextAsync(RepoFile("docs", "decisions", "DECISIONS-v1.31.md"), Ct); // B-01.7 and B-26 of 1.31
        await using var c = new NpgsqlConnection(db.ApiConnectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("SELECT key, text FROM hazem.approved_texts WHERE active AND source LIKE 'DECISIONS%'", c);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        // TASK 08: the system fills B-08's [brackets] itself; stored as {{slots}}, they read back as DECISIONS writes them.
        var slots = new Dictionary<string, string>
        {
            ["{{service}}"] = "[الخدمة]", ["{{day}}"] = "[اليوم]", ["{{time}}"] = "[الساعة]", ["{{amount}}"] = "[المبلغ]",
            ["{{instapay}}"] = "[العنوان]", ["{{wallet}}"] = "[الرقم]",
            ["{{attendees}}"] = "الزوج والزوجة والولي [والشاهدين]", // TASK 11: B-11.7, filled from the answers (a divorce: «الطرفين»)
            ["{{details}}"] = "[التفاصيل]", // TASK 12: B-18's details line (the office, the call, or who comes)
            ["{{first_name}}"] = "[الاسم الأول]", // 1.31 (B-01.7): the first word of the stored name
            // 1.25: where the lawyer calls, and the office's details in the meeting's confirmation.
            ["{{call_app}}"] = "[واتساب على نفس الرقم ده / FaceTime على نفس الرقم ده / لينك Google Meet ده: …]",
            ["{{address}}"] = "[العنوان]", ["{{map}}"] = "[الخريطة]", ["{{office_phone}}"] = "[رقم تليفون المكتب]",
            // TASK 06: B-23's prices, filled from the panel's price list (its starting values are DECISIONS' numbers).
            ["{{price_phone}}"] = "500", ["{{price_office}}"] = "1,000", ["{{price_urgent}}"] = "1,000", ["{{price_review_min}}"] = "1,500",
            ["{{price_review_max}}"] = "3,000", ["{{price_drafting_from}}"] = "3,000", ["{{price_notice}}"] = "2,000",
            ["{{price_opinion}}"] = "2,000", ["{{price_urf_office}}"] = "5,000", ["{{price_urf_own}}"] = "4,000",
        };
        var count = 0;
        while (await r.ReadAsync(Ct))
        {
            count++;
            var text = slots.Aggregate(r.GetString(1), (t, s) => t.Replace(s.Key, s.Value, StringComparison.Ordinal));
            Assert.True(decisions.Contains(text, StringComparison.Ordinal), $"{r.GetString(0)} is not word for word in DECISIONS");
        }

        Assert.Equal(85, count); // 8 (TASK 04) + B-04 question + the two B-25 texts (TASK 05a) + B-08 and B-22 (TASK 08) + B-11 (TASK 11) + B-18 (TASK 12) + B-11.1 (v1.20) + 9 greetings B-01.6 (v1.21) + 5 of v1.22 + 8 of v1.25 + 3 of v1.28 + ask_name_again of v1.29 (welcome_ask_name's v2 replaced its v1) + the 22 of the services list (v1.30: 13 texts and 9 price lines) + 11 of v1.31 (welcome_back and the 10 of the rating)
    }

    [Fact]
    public async Task No_active_text_has_an_open_office_placeholder() =>
        Assert.Equal(0L, await db.ScalarAsync<long>(
            @"SELECT count(*) FROM hazem.approved_texts WHERE active AND regexp_replace(text, '\{\{[a-z_]+\}\}', '', 'g') ~ '\{\{|\['"));

    [Fact]
    public async Task Migration_0007_activated_exactly_the_23_proposals_approved_by_the_owner() =>
        // TASK 06: six of them quote a price and have a newer version with the price slots (0018): the key is what stays active.
        Assert.Equal("23|23", await db.ScalarAsync<string>("""
            SELECT count(*) FILTER (WHERE EXISTS (SELECT 1 FROM hazem.approved_texts n WHERE n.key = t.key AND n.active)) || '|' || count(*)
              FROM hazem.approved_texts t
             WHERE t.source LIKE 'proposal TASK 04%' AND t.updated_by = 'owner approval 2026-10-22'
            """));

    [Theory]
    [InlineData("persons")]
    [InlineData("approved_texts")]
    [InlineData("classifications")]
    public async Task Office_app_is_blocked_and_readonly_can_read_the_new_tables(string table)
    {
        await using (var app = new NpgsqlConnection(db.AppConnectionString))
        {
            await app.OpenAsync(Ct);
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM hazem.{table}", app);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync(Ct))).SqlState);
        }

        Assert.True(await db.ScalarAsync<bool>($"SELECT has_table_privilege('office_readonly', 'hazem.{table}', 'SELECT') AND NOT has_table_privilege('office_readonly', 'hazem.{table}', 'INSERT') AND has_table_privilege('office_api', 'hazem.{table}', 'INSERT')"));
    }

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(Environment.GetEnvironmentVariable("OFFICE_TEST_REPO_DIR") ?? AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"{Path.Combine(parts)} not found; set OFFICE_TEST_REPO_DIR.");
    }
}
