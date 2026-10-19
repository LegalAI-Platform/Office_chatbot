using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Office.Core.WhatsApp;
using Office.Infrastructure;
using Office.Infrastructure.Persistence;
using Office.Infrastructure.WhatsApp;

// Office.Cli — operational commands. Every command prints JSON and exits non-zero on failure.
//   migrate [--to <migration>]        EF migrations + Hangfire schema (as office_migrator)
//   migrations-list
//   wa-subscribe --callback <https>   register/update the Meta webhook for this app
//   wa-subscribe-waba                 make sure the WABA sends events to this app
//   wa-check                          current subscription, WABA apps, phone number status
//   templates-submit                  submit catalog templates Meta does not have yet
//   templates-sync                    pull template statuses into app.wa_templates
//   send-test --to <wa_id> --text "…" send a text to a TestRecipients number only

var command = args.FirstOrDefault() ?? "help";
string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var builder = Host.CreateApplicationBuilder(args.Length > 0 ? args[1..] : []);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None); // URLs may carry tokens
// EF logs an expected failure while probing for the history table on a fresh database;
// real failures still surface as the command's JSON error.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Services.AddOfficeInfrastructure(builder.Configuration,
    command == "migrate" || command == "migrations-list" ? DependencyInjection.MigratorConnectionName : DependencyInjection.AppConnectionName);
builder.Services.AddScoped<DatabaseMigrator>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var sp = scope.ServiceProvider;
var ct = CancellationToken.None;

try
{
    JsonNode output = command switch
    {
        "migrate" => await Migrate(),
        "migrations-list" => await ListMigrations(),
        "wa-subscribe" => await RequireWhatsApp(() => sp.GetRequiredService<MetaAdmin>().SubscribeWebhookAsync(
            Option("--callback") ?? throw new ArgumentException("--callback <https-url> is required"), ct)),
        "wa-subscribe-waba" => await RequireWhatsApp(() => sp.GetRequiredService<MetaAdmin>().SubscribeWabaAsync(ct)),
        "wa-check" => await RequireWhatsApp(() => sp.GetRequiredService<MetaAdmin>().CheckAsync(ct)),
        "templates-submit" => await RequireWhatsApp(() => sp.GetRequiredService<MetaAdmin>().SubmitTemplatesAsync(ct)),
        "templates-sync" => await RequireWhatsApp(() => sp.GetRequiredService<MetaAdmin>().SyncTemplatesAsync(ct)),
        "send-test" => await SendTest(),
        _ => Help(),
    };

    Console.WriteLine(output.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    return output["ok"]?.GetValue<bool>() == false ? 1 : 0;
}
catch (Exception ex)
{
    Console.WriteLine(new JsonObject { ["ok"] = false, ["error"] = ex.GetType().Name, ["message"] = ex.Message }.ToJsonString());
    return 1;
}

async Task<JsonNode> Migrate()
{
    var pending = await sp.GetRequiredService<DatabaseMigrator>().MigrateAsync(Option("--to"), ct);
    return new JsonObject { ["ok"] = true, ["applied"] = new JsonArray(pending.Select(p => (JsonNode?)p).ToArray()), ["hangfire_schema"] = DatabaseMigrator.HangfireSchema };
}

async Task<JsonNode> ListMigrations()
{
    var (applied, pending) = await sp.GetRequiredService<DatabaseMigrator>().ListAsync(ct);
    return new JsonObject
    {
        ["ok"] = true,
        ["applied"] = new JsonArray(applied.Select(p => (JsonNode?)p).ToArray()),
        ["pending"] = new JsonArray(pending.Select(p => (JsonNode?)p).ToArray()),
    };
}

async Task<JsonNode> RequireWhatsApp(Func<Task<JsonObject>> action)
{
    if (!sp.GetRequiredService<IWhatsAppSettings>().Enabled)
    {
        return new JsonObject { ["ok"] = false, ["error"] = "WhatsApp__Enabled is false; fill the WhatsApp__* values in .env (docs/meta-setup.md)." };
    }

    var result = await action();
    result["ok"] ??= true;
    return result;
}

async Task<JsonNode> SendTest()
{
    var settings = sp.GetRequiredService<IWhatsAppSettings>();
    var to = Option("--to") ?? throw new ArgumentException("--to <wa_id> is required");
    var text = Option("--text") ?? "رسالة اختبار من النظام ✅";
    if (!settings.TestRecipients.Contains(to))
    {
        return new JsonObject { ["ok"] = false, ["error"] = $"{PhoneMask.Mask(to)} is not in WhatsApp__TestRecipients" };
    }

    var result = await sp.GetRequiredService<MessageSender>()
        .SendTextAsync(to, text, "send-test:" + Guid.CreateVersion7(), "cli send-test", null, ct);
    return JsonSerializer.SerializeToNode(result)!;
}

static JsonNode Help() => new JsonObject
{
    ["ok"] = false,
    ["usage"] = "migrate [--to X] | migrations-list | wa-subscribe --callback URL | wa-subscribe-waba | wa-check | templates-submit | templates-sync | send-test --to WA_ID [--text TEXT]",
};
