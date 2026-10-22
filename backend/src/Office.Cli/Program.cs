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
        _ => new JsonObject
        {
            ["ok"] = false,
            ["usage"] = "send-test --to WA_ID [--text TEXT] | classify-eval [--model M] [--limit N] [--input-price USD --output-price USD] < eval-set.jsonl",
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
    int total = 0, correct = 0, serviceChecked = 0, serviceCorrect = 0;
    long latency = 0, inputTokens = 0, outputTokens = 0;
    var mistakes = new JsonArray();
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

        if ((string?)sample["service"] is { } service)
        {
            serviceChecked++;
            serviceCorrect += c.Service is { } s && Classification.Code(s) == service ? 1 : 0;
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
