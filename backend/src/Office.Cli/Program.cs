using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Office.Application.Common;
using Office.Application.Ports;
using Office.Application.Responders;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Application.UseCases;
using Office.Infrastructure;
using Office.Infrastructure.Ai;
using Office.Infrastructure.Json;
using Office.Application.Panel;
using Office.Infrastructure.Lists;

// Office.Cli — operational commands, run inside the api container:
//   docker compose exec api dotnet /app/cli/Office.Cli.dll <command>
// Every command prints JSON and exits non-zero on failure.
//   send-test --to <wa_id> [--text "…"]   send a text through n8n (API -> n8n -> Meta);
//                                         only to numbers listed in hazem.settings echo.recipients
//   classify-eval [--file F] [--model M] [--limit N] [--input-price USD --output-price USD]
//                                         reads docs/ai/eval-set.jsonl (--file, else stdin) and classifies
//                                         every line with the REAL OpenAI key (HAZEM_OPENAI_API_KEY):
//                                         accuracy per intent, confusion matrix, latency, tokens,
//                                         and the cost when prices (USD per 1M tokens) are given.
//                                         The owner runs it (T-08); it never runs in the tests.
//     docker compose cp docs/ai/eval-set.jsonl api:/tmp/eval-set.jsonl
//     docker compose exec api dotnet /app/cli/Office.Cli.dll classify-eval --file /tmp/eval-set.jsonl
//   import-lists --kind blocked|lawyer_direct|existing_client --file <csv> [--replace]
//                                         B-14 lists from CSV UTF-8 (name,phone[,staff]); bad lines are
//                                         reported by line number, never with the whole number
//   tasks-overdue                         marks staff tasks past due_at as overdue (S-05)
//   unmute --to <wa_id>                   the manager brings a muted conversation back (S-01)
//   calendar-check [--calendar ID] [--write]  TASK 08: can the API read (and with --write, write: one test event
//                                         made and deleted at once) the lawyer's Google Calendar? (the key in
//                                         .env, the calendar of booking.calendar_id or --calendar shared with
//                                         the service account). Prints the account's e-mail, never the key
// Panel accounts (TASK 07a, T-23): the first manager and emergencies only; the manager manages
// every other account from the panel. Passwords are typed in the terminal (twice, hidden) or, for
// scripts, read from stdin; never an argument, never printed.
//   docker compose exec -it api dotnet /app/cli/Office.Cli.dll create-user --name owner --display "…" --email … --role manager
//   create-user --name N --display D --email E --role manager|lawyer|staff [--staff NAME]
//   reset-password --name N               a new password, typed in the terminal; unlocks, ends every session
//   unlock-user --name N                  clears a lockout (5 wrong passwords = 15 minutes)
//   reset-2fa --name N                    turns the authenticator off (a lost phone and no other manager, v1.9)
//   list-users                            every account, never a hash

var command = args.FirstOrDefault() ?? "help";
string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var builder = Host.CreateApplicationBuilder(args.Length > 0 ? args[1..] : []);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Services.AddOfficeInfrastructure(builder.Configuration);

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var sp = scope.ServiceProvider;
var ct = CancellationToken.None;

try
{
    JsonNode output = command switch
    {
        "send-test" => await SendTest(),
        "classify-eval" => await ClassifyEval(),
        "import-lists" => await ImportLists(),
        "tasks-overdue" => await TasksOverdue(),
        "unmute" => await Unmute(),
        "calendar-check" => await CalendarCheck(),
        "create-user" => await CreateUser(),
        "reset-password" => await ResetPassword(),
        "unlock-user" => await UnlockUser(),
        "reset-2fa" => await ResetTwoFactor(),
        "list-users" => await ListUsers(),
        _ => new JsonObject
        {
            ["ok"] = false,
            ["usage"] = "send-test --to WA_ID [--text TEXT] | classify-eval [--file F] [--model M] [--limit N] [--input-price USD --output-price USD] | import-lists --kind K --file CSV [--replace] | tasks-overdue | unmute --to WA_ID | create-user --name N --display D --email E --role R [--staff S] | reset-password --name N | unlock-user --name N | reset-2fa --name N | list-users",
        },
    };

    Console.WriteLine(output.ToJsonString(new JsonSerializerOptions(HazemJson.Options) { WriteIndented = true }));
    return output["ok"]?.GetValue<bool>() == true ? 0 : 1;
}
catch (Exception ex)
{
    Console.WriteLine(new JsonObject { ["ok"] = false, ["error"] = ex.GetType().Name, ["message"] = ex.Message }.ToJsonString());
    return 1;
}

async Task<JsonNode> SendTest()
{
    var to = Option("--to") ?? throw new ArgumentException("--to <wa_id> is required");
    var settings = await sp.GetRequiredService<ISettingsReader>().GetAsync(ct);
    if (!settings.TestRecipients.Contains(to))
    {
        return new JsonObject { ["ok"] = false, ["error"] = $"{LogMask.Phone(to)} is not in hazem.settings echo.recipients" };
    }

    var message = new OutboundMessage(ActionType.SendText, to, "cli-send-test:" + Guid.CreateVersion7(), "cli send-test",
        Text: Option("--text") ?? "رسالة اختبار من النظام ✅");
    var result = await sp.GetRequiredService<SendOutbound>().SendAsync(message, ct);
    return JsonSerializer.SerializeToNode(result, HazemJson.Options)!;
}

async Task<JsonNode> ImportLists()
{
    var kind = Option("--kind") ?? throw new ArgumentException("--kind blocked|lawyer_direct|existing_client is required");
    var file = Option("--file") ?? throw new ArgumentException("--file <csv> is required");
    using var reader = new StreamReader(file, System.Text.Encoding.UTF8);
    var r = await sp.GetRequiredService<ListImporter>().ImportAsync(kind, reader, args.Contains("--replace"), ct);
    return new JsonObject
    {
        ["ok"] = r.Rejected.Count == 0,
        ["kind"] = kind,
        ["imported"] = r.Imported,
        ["rejected"] = new JsonArray(r.Rejected.Select(x => (JsonNode)new JsonObject { ["line"] = x.Line, ["reason"] = x.Reason }).ToArray()),
    };
}

async Task<JsonNode> TasksOverdue() =>
    new JsonObject { ["ok"] = true, ["marked_overdue"] = await sp.GetRequiredService<TaskMaintenance>().MarkOverdueAsync(ct) };

// TASK 08: the same freeBusy call the booking makes, for today and the next 14 days.
async Task<JsonNode> CalendarCheck()
{
    var google = sp.GetRequiredService<Office.Infrastructure.Booking.GoogleCalendar>();
    var stored = await sp.GetRequiredService<ISettingsReader>().GetAsync(ct);
    var values = stored.Values.ToDictionary(kv => kv.Key, kv => kv.Value);
    if (Option("--calendar") is { Length: > 0 } id)
    {
        values[Office.Application.Booking.BookingSettings.CalendarIdKey] = id;
    }

    var settings = new HazemSettings(values);
    var calendar = settings.Get(Office.Application.Booking.BookingSettings.CalendarIdKey);
    var now = DateTimeOffset.UtcNow;
    var busy = await google.BusyAsync(now, now.AddDays(14), settings, ct);
    // --write: one test event («حازم: اختبار الربط») tomorrow at 03:00 Cairo for 5 minutes, deleted at once.
    string? write = null;
    if (busy.Available && args.Contains("--write"))
    {
        var day = Office.Application.Booking.BookingDesk.Today(now).AddDays(1);
        var start = Office.Domain.Booking.Availability.At(day, 180, Office.Application.Booking.BookingDesk.Zone)!.Value;
        var eventId = await google.CreateEventAsync(settings, "حازم: اختبار الربط", start, start.AddMinutes(5), Guid.Empty, ct);
        write = eventId is null ? "create_failed: is it shared with «Make changes to events»?"
            : await google.DeleteEventAsync(settings, eventId, ct) ? "created_and_deleted" : "created_but_not_deleted (delete it by hand)";
    }

    return new JsonObject
    {
        ["ok"] = busy.Available,
        ["key_in_env"] = google.HasKey,
        ["service_account"] = google.ServiceAccountEmail,
        ["calendar"] = calendar,
        ["problem"] = busy.Problem switch
        {
            null => null,
            "not_configured" when !google.HasKey => "GOOGLE_SERVICE_ACCOUNT_JSON is empty or unreadable",
            "not_configured" => "no calendar id: set it in the panel (إعدادات الحجز) or pass --calendar",
            "calendar_error" => "Google answered for the calendar with an error: is it shared with the service account?",
            "http_403" => "403: is the Google Calendar API enabled in the service account's project?",
            "http_404" => "404: no such calendar for this account (not shared, or a wrong id)",
            var p => p,
        },
        ["busy_times_next_14_days"] = busy.Available ? busy.Busy.Count : null,
        ["write_test"] = write,
    };
}

async Task<JsonNode> Unmute()
{
    var to = Option("--to") ?? throw new ArgumentException("--to <wa_id> is required");
    var (outcome, error) = await sp.GetRequiredService<StaffEvents>().UnmuteAsync(to, ct);
    return new JsonObject { ["ok"] = outcome == StaffEvents.Outcome.Done, ["wa_id"] = LogMask.Phone(to), ["result"] = outcome.ToString(), ["error"] = error };
}

async Task<JsonNode> ClassifyEval()
{
    var options = sp.GetRequiredService<IOptions<HazemOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.OpenAiApiKey))
    {
        return new JsonObject { ["ok"] = false, ["error"] = "HAZEM_OPENAI_API_KEY is empty: classify-eval needs a real key (docs/ai/classification.md)" };
    }

    if (Option("--model") is { Length: > 0 } model)
    {
        options.OpenAiModel = model; // IOptions holds one instance: the classifier reads this value
    }

    // The real OpenAI, whatever hazem-e2e may have left in ai.stub_*.
    var db = await sp.GetRequiredService<ISettingsReader>().GetAsync(ct);
    var settings = new HazemSettings(new Dictionary<string, string>(db.Values) { ["ai.stub_enabled"] = "false", [Classification.MinConfidenceKey] = "0" });
    var classifier = sp.GetRequiredService<IIntentClassifier>();
    var limit = int.TryParse(Option("--limit"), out var l) ? l : int.MaxValue;

    var confusion = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
    var failures = new SortedDictionary<string, int>(StringComparer.Ordinal);
    int total = 0, correct = 0, serviceChecked = 0, serviceCorrect = 0, matterChecked = 0, matterCorrect = 0, roleChecked = 0, roleCorrect = 0,
        topicChecked = 0, topicCorrect = 0;
    var otherMistakes = new JsonArray();
    long latency = 0, inputTokens = 0, outputTokens = 0;
    var mistakes = new JsonArray();
    var serviceMistakes = new JsonArray();
    using var input = Option("--file") is { Length: > 0 } file ? new StreamReader(file) : new StreamReader(Console.OpenStandardInput());
    while (total < limit && await input.ReadLineAsync(ct) is { } line)
    {
        line = line.Trim().TrimStart('﻿'); // Windows PowerShell may prepend a BOM
        if (line.Length == 0)
        {
            continue;
        }

        var sample = JsonNode.Parse(line)!;
        var expected = (string)sample["intent"]!;
        var outcome = await classifier.ClassifyAsync(AiInput.Redact((string)sample["text"]!), settings, ct);
        var c = Classification.From(outcome, settings);
        var got = c.IsFallback ? $"other ({c.FallbackReason})" : Classification.Code(c.Intent);
        if (c.FallbackReason is { } reason)
        {
            failures[reason] = failures.GetValueOrDefault(reason) + 1;
        }

        total++;
        latency += outcome.LatencyMs;
        inputTokens += outcome.InputTokens ?? 0;
        outputTokens += outcome.OutputTokens ?? 0;
        var row = confusion.TryGetValue(expected, out var r) ? r : confusion[expected] = new SortedDictionary<string, int>(StringComparer.Ordinal);
        row[got] = row.GetValueOrDefault(got) + 1;
        if (got == expected)
        {
            correct++;
        }
        else
        {
            mistakes.Add(new JsonObject { ["id"] = sample["id"]?.DeepClone(), ["expected"] = expected, ["got"] = got });
        }

        // B-25 topic and B-14.4 role: scored only on samples that carry them.
        if ((string?)sample["matter_type"] is { } matter)
        {
            matterChecked++;
            var gotMatter = c.Matter is { } m ? Classification.Code(m) : null;
            if (gotMatter == matter)
            {
                matterCorrect++;
            }
            else
            {
                otherMistakes.Add(new JsonObject { ["id"] = sample["id"]?.DeepClone(), ["field"] = "matter_type", ["expected"] = matter, ["got"] = gotMatter });
            }
        }

        // TASK 06: the office question's topic, on the samples that carry one.
        if ((string?)sample["info_topic"] is { } topic)
        {
            topicChecked++;
            var gotTopic = c.Topic is { } t ? Classification.Code(t) : null;
            if (gotTopic == topic)
            {
                topicCorrect++;
            }
            else
            {
                otherMistakes.Add(new JsonObject { ["id"] = sample["id"]?.DeepClone(), ["field"] = "info_topic", ["expected"] = topic, ["got"] = gotTopic });
            }
        }

        if ((string?)sample["sender_role"] is { } role)
        {
            roleChecked++;
            var gotRole = Classification.Code(c.Role);
            if (gotRole == role)
            {
                roleCorrect++;
            }
            else
            {
                otherMistakes.Add(new JsonObject { ["id"] = sample["id"]?.DeepClone(), ["field"] = "sender_role", ["expected"] = role, ["got"] = gotRole });
            }
        }

        if ((string?)sample["service"] is { } service)
        {
            serviceChecked++;
            var gotService = c.Service is { } s ? Classification.Code(s) : null;
            if (gotService == service)
            {
                serviceCorrect++;
            }
            else
            {
                serviceMistakes.Add(new JsonObject { ["id"] = sample["id"]?.DeepClone(), ["expected"] = service, ["got"] = gotService });
            }
        }
    }

    var perIntent = new JsonObject();
    foreach (var (intent, row) in confusion)
    {
        var n = row.Values.Sum();
        perIntent[intent] = new JsonObject { ["samples"] = n, ["accuracy"] = Math.Round((double)row.GetValueOrDefault(intent) / n, 3) };
    }

    var result = new JsonObject
    {
        ["ok"] = total > 0,
        ["model"] = options.OpenAiModel,
        ["prompt_version"] = OpenAiIntentClassifier.PromptVersion,
        ["samples"] = total,
        ["accuracy"] = total == 0 ? 0 : Math.Round((double)correct / total, 3),
        ["service_accuracy"] = serviceChecked == 0 ? null : Math.Round((double)serviceCorrect / serviceChecked, 3),
        ["per_intent"] = perIntent,
        ["confusion"] = JsonSerializer.SerializeToNode(confusion),
        ["fallbacks"] = JsonSerializer.SerializeToNode(failures),
        ["avg_latency_ms"] = total == 0 ? 0 : latency / total,
        ["input_tokens"] = inputTokens,
        ["output_tokens"] = outputTokens,
        ["mistakes"] = mistakes,
        ["service_mistakes"] = serviceMistakes,
        ["matter_accuracy"] = matterChecked == 0 ? null : Math.Round((double)matterCorrect / matterChecked, 3),
        ["sender_role_accuracy"] = roleChecked == 0 ? null : Math.Round((double)roleCorrect / roleChecked, 3),
        ["info_topic_accuracy"] = topicChecked == 0 ? null : Math.Round((double)topicCorrect / topicChecked, 3),
        ["other_mistakes"] = otherMistakes,
    };
    if (double.TryParse(Option("--input-price"), System.Globalization.CultureInfo.InvariantCulture, out var inPrice)
        && double.TryParse(Option("--output-price"), System.Globalization.CultureInfo.InvariantCulture, out var outPrice))
    {
        var cost = (inputTokens * inPrice + outputTokens * outPrice) / 1_000_000;
        result["cost_usd"] = Math.Round(cost, 4);
        result["cost_per_1000_turns_usd"] = total == 0 ? 0 : Math.Round(cost / total * 1000, 2);
    }

    return result;
}

async Task<JsonNode> CreateUser()
{
    var name = Option("--name") ?? throw new ArgumentException("--name <user name> is required");
    var input = new PanelAccounts.NewAccount(name, Option("--display") ?? name, Option("--email"), Option("--role"), Option("--staff"));
    var r = await sp.GetRequiredService<PanelAccounts>().CreateAsync(null, input, ReadPassword(), ct);
    return r.Outcome == PanelOutcome.Ok
        ? new JsonObject { ["ok"] = true, ["user"] = r.Value.User.Username, ["role"] = r.Value.User.Role }
        : new JsonObject { ["ok"] = false, ["error"] = r.Error, ["fields"] = JsonSerializer.SerializeToNode(r.Fields, HazemJson.Options) };
}

async Task<JsonNode> ResetPassword()
{
    var accounts = sp.GetRequiredService<PanelAccounts>();
    var user = await FindUser(accounts);
    var r = await accounts.ResetPasswordAsync(null, user.Id, ReadPassword(), ct);
    return new JsonObject { ["ok"] = r.Outcome == PanelOutcome.Ok, ["user"] = user.Username, ["error"] = r.Error };
}

async Task<JsonNode> UnlockUser()
{
    var accounts = sp.GetRequiredService<PanelAccounts>();
    var user = await FindUser(accounts);
    var r = await accounts.UnlockAsync(user.Id, ct);
    return new JsonObject { ["ok"] = r.Outcome == PanelOutcome.Ok, ["user"] = user.Username };
}

async Task<JsonNode> ResetTwoFactor()
{
    var user = await FindUser(sp.GetRequiredService<PanelAccounts>());
    var r = await sp.GetRequiredService<PanelTwoFactor>().ResetAsync(null, user.Id, ct);
    return new JsonObject { ["ok"] = r.Outcome == PanelOutcome.Ok, ["user"] = user.Username, ["error"] = r.Error };
}

async Task<JsonNode> ListUsers()
{
    var now = DateTimeOffset.UtcNow;
    var users = await sp.GetRequiredService<PanelAccounts>().ListAsync(ct);
    return new JsonObject
    {
        ["ok"] = true,
        ["users"] = new JsonArray(users.Select(u => (JsonNode)new JsonObject
        {
            ["user"] = u.Username, ["display_name"] = u.DisplayName, ["role"] = u.Role, ["active"] = u.Active,
            ["locked"] = u.IsLocked(now), ["must_change_password"] = u.MustChangePassword,
        }).ToArray()),
    };
}

async Task<PanelUser> FindUser(PanelAccounts accounts)
{
    var name = Option("--name") ?? throw new ArgumentException("--name <user name> is required");
    return await accounts.FindByUsernameAsync(name, ct) ?? throw new ArgumentException($"no account named {name}");
}

// A password typed twice without echo, or one line from stdin when it is redirected (scripts, tests).
static string ReadPassword()
{
    if (Console.IsInputRedirected)
    {
        // Windows PowerShell 5.1 puts a BOM in front of what it pipes into docker.
        return (Console.In.ReadLine() ?? throw new ArgumentException("no password on stdin")).TrimStart('﻿');
    }

    var first = Prompt("Password (12+ characters): ");
    if (Prompt("Again: ") != first)
    {
        throw new ArgumentException("the two passwords differ");
    }

    return first;

    static string Prompt(string label)
    {
        Console.Error.Write(label);
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return sb.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
            }
        }
    }
}
