using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Office.Core.WhatsApp;

namespace Office.Infrastructure.WhatsApp;

/// <summary>
/// Bound from the "WhatsApp" section (env: WhatsApp__AppId, ...). AppSecret, AccessToken and
/// VerifyToken are secrets and come from environment variables only (T-21).
/// </summary>
public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";

    public bool Enabled { get; set; }
    public string AppId { get; set; } = "";
    public string WabaId { get; set; } = "";
    public string PhoneNumberId { get; set; } = "";
    public string GraphApiVersion { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string VerifyToken { get; set; } = "";

    /// <summary>Comma-separated wa_id list allowed to receive test messages.</summary>
    public string TestRecipients { get; set; } = "";

    public EchoTestOptions EchoTest { get; set; } = new();

    public IReadOnlyList<string> TestRecipientList =>
        TestRecipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public sealed class EchoTestOptions
    {
        public bool Enabled { get; set; }
    }
}

public sealed partial class WhatsAppOptionsValidator : IValidateOptions<WhatsAppOptions>
{
    [GeneratedRegex(@"^v\d+\.\d+$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9]{8,15}$")]
    private static partial Regex WaIdPattern();

    public ValidateOptionsResult Validate(string? name, WhatsAppOptions o)
    {
        var errors = new List<string>();
        foreach (var r in o.TestRecipientList.Where(r => !WaIdPattern().IsMatch(r)))
        {
            errors.Add($"WhatsApp:TestRecipients contains an invalid number ({PhoneMask.Mask(r)}); use digits only, e.g. 2010xxxxxxxx.");
        }

        if (!o.Enabled)
        {
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }

        Require(o.AppId, "WhatsApp:AppId");
        Require(o.WabaId, "WhatsApp:WabaId");
        Require(o.PhoneNumberId, "WhatsApp:PhoneNumberId");
        Require(o.AppSecret, "WhatsApp:AppSecret");
        Require(o.AccessToken, "WhatsApp:AccessToken");
        Require(o.VerifyToken, "WhatsApp:VerifyToken");
        if (!VersionPattern().IsMatch(o.GraphApiVersion))
        {
            errors.Add("WhatsApp:GraphApiVersion must look like v24.0.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);

        void Require(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains("CHANGE_ME", StringComparison.Ordinal))
            {
                errors.Add($"{key} is required when WhatsApp:Enabled is true.");
            }
        }
    }
}

/// <summary>Exposes the non-secret settings the Core use cases need.</summary>
public sealed class WhatsAppSettings(IOptions<WhatsAppOptions> options, IHostEnvironment env) : IWhatsAppSettings
{
    public bool Enabled => options.Value.Enabled;
    public IReadOnlyCollection<string> TestRecipients => options.Value.TestRecipientList;

    /// <summary>The echo test may only run in the test environment.</summary>
    public bool EchoTestEnabled => options.Value.EchoTest.Enabled && env.EnvironmentName == "test";
}
