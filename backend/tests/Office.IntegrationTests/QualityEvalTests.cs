using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Messages;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 18 §3.1: the conversation test suite. Every conversation of docs/ai/conversations.jsonl goes through the real Hazem (the
/// API in-process on the throwaway database, the fake calendar, the real classifier and writer as on the test server), and a
/// judge (Claude Opus, docs/ai/judge-v1.md with docs/ai/judge-facts.md) scores every reply. The report goes to
/// docs/ai/quality/&lt;QUALITY_LABEL&gt;.md and .json. It costs money: it runs only with QUALITY_EVAL=1 and the owner's
/// HAZEM_ANTHROPIC_API_KEY (scripts quality-eval), never in backend-test. QUALITY_ONLY=id1,id2 runs some conversations only.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class QualityEvalTests(TestDatabase db)
{
    private const string JudgeModel = "claude-opus-5-5";
    private const string WriterModel = "claude-haiku-5-5"; // the test server's writer
    private const int Workers = 4;
    private const int Judges = 6;
    private const string Address = "15 شارع التحرير، الدقي، الجيزة (الدور التالت)";
    private static readonly DateTimeOffset DefaultTime = new(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly string[] Scores = ["answered", "facts", "next_step", "coherent", "tone"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.appointment_reminders;
        UPDATE hazem.appointments SET urgent_alert_receipt = NULL;
        DELETE FROM hazem.urf_files; DELETE FROM hazem.urf_cases;
        DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments; DELETE FROM hazem.service_files; DELETE FROM hazem.service_orders;
        DELETE FROM hazem.appointments; DELETE FROM hazem.booking_exceptions; DELETE FROM hazem.booking_changes;
        UPDATE hazem.settings SET value = 'false' WHERE key IN ('booking.enabled', 'urgent.alert_in_window', 'hazem.open_to_all', 'hazem.conversational');
        UPDATE hazem.settings SET value = '' WHERE key IN ('booking.calendar_id', 'office.instapay', 'office.wallet_number', 'office.wallet_provider',
          'office.lawyer_whatsapp', 'office.manager_whatsapp', 'office.address', 'office.map_url', 'ai.writer_model');
        UPDATE hazem.settings SET value = 'openai' WHERE key = 'ai.provider';
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.generated_replies, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
        DELETE FROM hazem.staff; DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
        """;

    [Fact]
    public async Task The_conversation_suite_is_played_through_Hazem_and_judged()
    {
        var key = Environment.GetEnvironmentVariable("HAZEM_ANTHROPIC_API_KEY");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("QUALITY_EVAL") == "1" && !string.IsNullOrWhiteSpace(key),
            "The conversation suite runs only from the quality-eval script (it calls the real AI).");
        var label = Environment.GetEnvironmentVariable("QUALITY_LABEL") is { Length: > 0 } l ? l : "baseline";
        var only = (Environment.GetEnvironmentVariable("QUALITY_ONLY") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var suiteFile = RepoFile("docs", "ai", "conversations.jsonl");
        var docs = Path.GetDirectoryName(suiteFile)!;
        var conversations = File.ReadAllLines(suiteFile).Where(s => s.Trim().Length > 0).Select(Conversation.Parse)
            .Where(c => only.Length == 0 || only.Contains(c.Id)).ToList();
        var judgeSystem = File.ReadAllText(Path.Combine(docs, "judge-v1.md")) + "\n\nOFFICE FACTS:\n"
            + File.ReadAllText(Path.Combine(docs, "judge-facts.md"))
            + $"\n- The office's address (in this test): {Address}. Hours: from 10:00 to midnight.\n";

        await db.ExecAsync(Clean);
        await db.SetAsync("responder", "hazem");
        await db.ExecAsync($"""
            UPDATE hazem.settings SET value = 'true' WHERE key IN ('booking.enabled', 'hazem.open_to_all', 'hazem.conversational');
            UPDATE hazem.settings SET value = 'anthropic' WHERE key = 'ai.provider';
            UPDATE hazem.settings SET value = '{WriterModel}' WHERE key = 'ai.writer_model';
            UPDATE hazem.settings SET value = 'lawyer@example.test' WHERE key = 'booking.calendar_id';
            UPDATE hazem.settings SET value = 'office@instapay' WHERE key = 'office.instapay';
            UPDATE hazem.settings SET value = '01000000000' WHERE key = 'office.wallet_number';
            UPDATE hazem.settings SET value = 'فودافون كاش' WHERE key = 'office.wallet_provider';
            UPDATE hazem.settings SET value = '{Address}' WHERE key = 'office.address';
            UPDATE hazem.settings SET value = 'https://maps.example.test/office' WHERE key = 'office.map_url';
            UPDATE hazem.settings SET value = '201000000901' WHERE key = 'office.lawyer_whatsapp';
            UPDATE hazem.settings SET value = '201000000902' WHERE key = 'office.manager_whatsapp';
            UPDATE hazem.approved_texts SET active = true
             WHERE source LIKE 'proposal TASK%' AND version = (SELECT max(m.version) FROM hazem.approved_texts m WHERE m.key = approved_texts.key);
            """);
        try
        {
            var played = new Played[conversations.Count];
            var queue = new System.Collections.Concurrent.ConcurrentQueue<int>(Enumerable.Range(0, conversations.Count));
            await Task.WhenAll(Enumerable.Range(0, Workers).Select(async _ =>
            {
                var clock = new MutableClock(DefaultTime.ToUniversalTime());
                await using var factory = new ApiFactory(db, clock) { RealAnthropicKey = key };
                using var n8n = factory.CreateN8nClient();
                while (queue.TryDequeue(out var i))
                {
                    played[i] = await Play(n8n, clock, conversations[i], i, factory.Logs);
                }
            }));

            var judge = new AnthropicClient { ApiKey = key!, MaxRetries = 3 };
            var usage = new long[2];
            using var gate = new SemaphoreSlim(Judges);
            var judged = await Task.WhenAll(played.SelectMany(p => p.Replies.Select((r, n) => (p, n))).Select(async x =>
            {
                await gate.WaitAsync(Ct);
                try
                {
                    return (x.p.Conversation.Id, x.n, Verdict: await Judge(judge, judgeSystem, x.p, x.n, usage));
                }
                finally
                {
                    gate.Release();
                }
            }));
            var verdicts = judged.ToDictionary(j => (j.Id, j.n), j => j.Verdict);
            var (hazemIn, hazemOut) = await HazemTokens();
            Report(Path.Combine(docs, "quality"), label, played, verdicts, usage, hazemIn, hazemOut);
        }
        finally
        {
            await db.ExecAsync(Clean);
        }
    }

    /// <summary>One conversation, turn by turn; a press that finds no such button ends it (the button was not shown).</summary>
    private static async Task<Played> Play(HttpClient n8n, MutableClock clock, Conversation c, int index, MemoryLogs logs)
    {
        // a week apart each (the same weekday and hour), so conversations never take each other's free times
        clock.Advance((c.Time ?? DefaultTime).AddDays(7 * index).ToUniversalTime() - clock.GetUtcNow());
        var waId = $"20109{index:D7}";
        var played = new Played(c, []);
        var history = new List<string>();
        var n = 0;
        foreach (var step in c.Turns)
        {
            JsonObject message;
            string said;
            if (step.Say is { } text)
            {
                message = new JsonObject { ["type"] = "text", ["text"] = text };
                said = text;
            }
            else if (step.Image)
            {
                message = new JsonObject
                {
                    ["type"] = "image",
                    ["media"] = new JsonObject
                    {
                        ["status"] = "stored", ["bucket"] = "office-sensitive", ["key"] = $"wa/2026/11/eval-{waId}-{n}.jpg",
                        ["mime_type"] = "image/jpeg", ["size_bytes"] = 3, ["sha256"] = $"eval{waId}{n}",
                    },
                };
                said = "[صورة]";
            }
            else
            {
                var menu = played.Replies.SelectMany(r => r.Actions).Select(a => a["interactive"]).OfType<JsonObject>().LastOrDefault();
                var choices = menu?["choices"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
                var choice = step.Pick is { } pick
                    ? choices.ElementAtOrDefault(pick)
                    : choices.FirstOrDefault(x => ((string?)x["title"] ?? "").Contains(step.Press!, StringComparison.Ordinal));
                if (choice is null)
                {
                    played.Missing = step.Press is null ? $"no list to pick row {step.Pick} from" : $"no button «{step.Press}»";
                    break;
                }

                var title = (string)choice["title"]!;
                message = new JsonObject
                {
                    ["type"] = "interactive",
                    ["interactive"] = new JsonObject
                    {
                        ["kind"] = (string?)menu!["kind"] == "list" ? "list_reply" : "button_reply", ["id"] = (string)choice["id"]!, ["title"] = title,
                    },
                };
                said = $"[ضغط: {title}]";
            }

            message["wamid"] = $"wamid.EVAL-{waId}-{++n}";
            history.Add("Client: " + said);
            var logged = logs.Lines.Count;
            var r = await n8n.PostAsJsonAsync("/api/hazem/turns", new JsonObject
            {
                ["turn_id"] = $"wt:eval-{waId}-{n}", ["wa_id"] = waId, ["contact_name"] = "Client",
                ["messages"] = new JsonArray(message),
            }, Ct);
            var actions = r.StatusCode == HttpStatusCode.OK
                ? (await r.Content.ReadFromJsonAsync<JsonNode>(Ct))!["actions"]!.AsArray().OfType<JsonObject>().ToList()
                : [new JsonObject { ["type"] = "send_text", ["text"] = $"(the API answered {(int)r.StatusCode})" }];
            if (r.StatusCode != HttpStatusCode.OK)
            {
                // the exception behind it, for the report (the logger of this worker's API only)
                played.Errors.AddRange(logs.Lines.Skip(logged).Where(l => l.Contains("Exception", StringComparison.Ordinal))
                    .Select(l => l.Length > 1500 ? l[..1500] : l));
            }

            var shown = actions.Count == 0 ? "(no reply)" : string.Join("\n", actions.Select(Show));
            played.Replies.Add(new Reply([.. history], shown, actions));
            history.Add("Hazem: " + shown);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        return played;
    }

    private static string Show(JsonObject a)
    {
        var text = (string?)a["text"] ?? (a["template_name"] is { } t ? $"(template {t})" : "");
        // a list row's description too (the prices under the services), as the client sees it
        var choices = a["interactive"]?["choices"]?.AsArray()
            .Select(x => (string?)x!["description"] is { Length: > 0 } d ? $"{(string?)x["title"]} ({d})" : (string?)x["title"]).ToList();
        return choices is { Count: > 0 } ? $"{text}\n[{string.Join(" | ", choices)}]" : text;
    }

    private static readonly Dictionary<string, JsonElement> JudgeSchema = new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["answered"] = Score(), ["facts"] = Score(), ["next_step"] = Score(), ["coherent"] = Score(), ["tone"] = Score(),
            ["forbidden"] = new JsonObject { ["type"] = "boolean" },
            ["reason"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("answered", "facts", "next_step", "coherent", "tone", "forbidden", "reason"),
        ["additionalProperties"] = false,
    }.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));

    private static JsonObject Score() => new() { ["type"] = "integer", ["enum"] = new JsonArray(0, 1, 2) };

    private static async Task<JsonObject> Judge(AnthropicClient client, string system, Played p, int n, long[] usage)
    {
        var reply = p.Replies[n];
        var user = $"""
            SCENARIO (about the whole conversation; judge this reply by what a good assistant does at this moment):
            {p.Conversation.Scenario}

            CONVERSATION:
            {string.Join("\n", reply.History)}

            REPLY:
            {reply.Shown}
            """;
        try
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = JudgeModel,
                MaxTokens = 4096,
                System = new List<TextBlockParam> { new() { Text = system, CacheControl = new CacheControlEphemeral() } },
                Messages = [new() { Role = Role.User, Content = user }],
                OutputConfig = new OutputConfig { Effort = Effort.Medium, Format = new JsonOutputFormat { Schema = JudgeSchema } },
            }, Ct);
            Interlocked.Add(ref usage[0], response.Usage.InputTokens + (response.Usage.CacheReadInputTokens ?? 0) + (response.Usage.CacheCreationInputTokens ?? 0));
            Interlocked.Add(ref usage[1], response.Usage.OutputTokens);
            var json = response.Content.Select(b => b.Value).OfType<TextBlock>().LastOrDefault()?.Text;
            return JsonNode.Parse(json ?? "")!.AsObject();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new JsonObject { ["error"] = ex.GetType().Name };
        }
    }

    private async Task<(long In, long Out)> HazemTokens()
    {
        var sql = """
            SELECT coalesce(sum(input_tokens), 0) || '|' || coalesce(sum(output_tokens), 0) FROM (
              SELECT input_tokens, output_tokens FROM hazem.classifications
              UNION ALL SELECT input_tokens, output_tokens FROM hazem.generated_replies) t
            """;
        var parts = (await db.ScalarAsync<string>(sql))!.Split('|');
        return (long.Parse(parts[0], CultureInfo.InvariantCulture), long.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static void Report(string folder, string label, Played[] played, Dictionary<(string, int), JsonObject> verdicts, long[] judgeUsage,
        long hazemIn, long hazemOut)
    {
        Directory.CreateDirectory(folder);
        var rows = new List<(Played P, int N, JsonObject V, double Score, bool Repeat)>();
        foreach (var p in played)
        {
            for (var n = 0; n < p.Replies.Count; n++)
            {
                var v = verdicts[(p.Conversation.Id, n)];
                var score = v["error"] is null ? Scores.Sum(s => (int)v[s]!) / 10.0 : 0;
                var texts = p.Replies[n].Actions.Select(a => (string?)a["text"]).Where(t => t is { Length: > 0 }).ToList();
                var repeat = n > 0 && texts.Count > 0 && p.Replies[n - 1].Actions.Any(a => texts.Contains((string?)a["text"]));
                rows.Add((p, n, v, score, repeat));
            }
        }

        var scored = rows.Where(r => r.V["error"] is null).ToList();
        double Pct(double x) => Math.Round(100 * x, 1);
        var overall = scored.Count == 0 ? 0 : scored.Average(r => r.Score);
        var good = scored.Count(r => r.Score == 1 && !(bool)r.V["forbidden"]!);
        var forbidden = scored.Count(r => (bool)r.V["forbidden"]!);
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Conversation suite — {label} — {DateTime.Now:yyyy-MM-dd HH:mm}");
        md.AppendLine();
        md.AppendLine("TASK 18 §3.1. Hazem as on the test server (classifier claude-opus-5-5, writer claude-haiku-5-5, conversational on), "
            + "judged by claude-opus-5-5 with docs/ai/judge-v1.md. A reply's score is its five marks (0–2) out of 10.");
        md.AppendLine();
        md.AppendLine("| Measure | Value |");
        md.AppendLine("|---|---|");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Conversations / replies judged | {played.Length} / {scored.Count} (judge errors: {rows.Count - scored.Count}) |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| **Overall score** | **{Pct(overall)}%** |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Replies with every mark 2 | {good} of {scored.Count} ({Pct(scored.Count == 0 ? 0 : (double)good / scored.Count)}%) |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Forbidden (law, a price not in the facts, a promise) | {forbidden} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Repeated the previous reply word for word | {rows.Count(r => r.Repeat)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| A button the conversation needed was not shown | {played.Count(p => p.Missing is not null)} |");
        foreach (var s in Scores)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {s} (average of 2) | {(scored.Count == 0 ? 0 : Math.Round(scored.Average(r => (int)r.V[s]!), 2))} |");
        }

        md.AppendLine(CultureInfo.InvariantCulture, $"| Tokens: Hazem in/out · judge in/out | {hazemIn:N0}/{hazemOut:N0} · {judgeUsage[0]:N0}/{judgeUsage[1]:N0} |");
        md.AppendLine();
        md.AppendLine("## By conversation");
        md.AppendLine();
        md.AppendLine("| Conversation | Score | Replies under 10/10 |");
        md.AppendLine("|---|---|---|");
        foreach (var g in rows.GroupBy(r => r.P.Conversation.Id))
        {
            var p = g.First().P;
            var flag = p.Missing is null ? "" : $" · stopped: {p.Missing}";
            md.AppendLine(CultureInfo.InvariantCulture, $"| {g.Key} | {Pct(g.Average(r => r.Score))}% | {g.Count(r => r.Score < 1)}{flag} |");
        }

        md.AppendLine();
        md.AppendLine("## Every reply under 10/10");
        foreach (var r in rows.Where(r => r.Score < 1 || r.Repeat || (bool?)r.V["forbidden"] == true))
        {
            md.AppendLine();
            var marks = r.V["error"] is null ? string.Join(" ", Scores.Select(s => $"{s} {(int)r.V[s]!}")) : $"judge error {r.V["error"]}";
            md.AppendLine(CultureInfo.InvariantCulture, $"### {r.P.Conversation.Id} · reply {r.N + 1} · {Pct(r.Score)}%{((bool?)r.V["forbidden"] == true ? " · **FORBIDDEN**" : "")}{(r.Repeat ? " · repeated" : "")}");
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"{marks} — {(string?)r.V["reason"]}");
            md.AppendLine();
            md.AppendLine("```text");
            md.AppendLine(r.P.Replies[r.N].History[^1]);
            md.AppendLine("Hazem: " + r.P.Replies[r.N].Shown);
            md.AppendLine("```");
        }

        foreach (var p in played.Where(p => p.Errors.Count > 0))
        {
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"## API error in {p.Conversation.Id}");
            md.AppendLine();
            md.AppendLine("```text");
            p.Errors.ForEach(e => md.AppendLine(e));
            md.AppendLine("```");
        }

        File.WriteAllText(Path.Combine(folder, $"{label}.md"), md.ToString());
        var json = new JsonArray(rows.Select(r => (JsonNode)new JsonObject
        {
            ["id"] = r.P.Conversation.Id, ["reply"] = r.N + 1, ["score"] = r.Score, ["repeat"] = r.Repeat,
            ["client"] = r.P.Replies[r.N].History[^1], ["hazem"] = r.P.Replies[r.N].Shown, ["verdict"] = r.V.DeepClone(),
        }).ToArray());
        File.WriteAllText(Path.Combine(folder, $"{label}.json"), json.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
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

    private sealed record Step(string? Say, string? Press, int? Pick, bool Image);

    private sealed record Conversation(string Id, string Scenario, DateTimeOffset? Time, IReadOnlyList<Step> Turns)
    {
        public static Conversation Parse(string line)
        {
            var o = JsonNode.Parse(line)!;
            return new Conversation((string)o["id"]!, (string)o["scenario"]!,
                o["time"] is { } t ? DateTimeOffset.Parse((string)t!, CultureInfo.InvariantCulture) : null,
                o["turns"]!.AsArray().Select(s => new Step((string?)s!["say"], (string?)s["press"], (int?)s["pick"], (bool?)s["image"] == true)).ToList());
        }
    }

    private sealed record Reply(IReadOnlyList<string> History, string Shown, IReadOnlyList<JsonObject> Actions);

    private sealed record Played(Conversation Conversation, List<Reply> Replies)
    {
        public string? Missing { get; set; }

        public List<string> Errors { get; } = [];
    }
}
