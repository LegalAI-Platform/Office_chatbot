using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Office.Core.Hazem;
using Office.Infrastructure;

// Office.Cli — operational commands, run inside the api container:
//   docker compose exec api dotnet /app/cli/Office.Cli.dll <command>
// Every command prints JSON and exits non-zero on failure.
//   send-test --to <wa_id> [--text "…"]   send a text through n8n (API -> n8n -> Meta);
//                                         only to numbers listed in hazem.settings echo.recipients

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
        _ => new JsonObject { ["ok"] = false, ["usage"] = "send-test --to WA_ID [--text TEXT]" },
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
        return new JsonObject { ["ok"] = false, ["error"] = $"{TurnRules.Mask(to)} is not in hazem.settings echo.recipients" };
    }

    var message = new OutboundMessage(TurnAction.SendText, to, "cli-send-test:" + Guid.CreateVersion7(), "cli send-test",
        Text: Option("--text") ?? "رسالة اختبار من النظام ✅");
    var result = await sp.GetRequiredService<OutboundService>().SendAsync(message, ct);
    return JsonSerializer.SerializeToNode(result, HazemJson.Options)!;
}
