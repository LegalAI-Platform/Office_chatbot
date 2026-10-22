using Microsoft.Extensions.Options;

namespace Office.Infrastructure;

/// <summary>Settings from the environment (Hazem__*); the two tokens come from .env (T-18).</summary>
public sealed class HazemOptions
{
    public const string Section = "Hazem";
    public const int MinTokenLength = 32;

    /// <summary>What n8n must send in X-Hazem-Token (HAZEM_API_TOKEN).</summary>
    public string ApiToken { get; set; } = "";

    /// <summary>n8n's internal send webhook, e.g. http://n8n:5678/webhook/hazem-internal-send.</summary>
    public string N8nInternalSendUrl { get; set; } = "";

    /// <summary>What the API sends to that webhook in X-Hazem-Token (N8N_INTERNAL_SEND_TOKEN).</summary>
    public string N8nInternalSendToken { get; set; } = "";

    /// <summary>
    /// HAZEM_OPENAI_API_KEY: the .NET key, separate from n8n's transcription key (T-08). May be
    /// empty: classification is then off and Hazem answers every text turn as unclear.
    /// </summary>
    public string OpenAiApiKey { get; set; } = "";

    /// <summary>
    /// OPENAI_MODEL. The default is a documented starting point only: the final model is chosen
    /// by the owner after running classify-eval on the sample set (T-08).
    /// </summary>
    public string OpenAiModel { get; set; } = DefaultOpenAiModel;

    /// <summary>OPENAI_BASE_URL.</summary>
    public string OpenAiBaseUrl { get; set; } = DefaultOpenAiBaseUrl;

    public const string DefaultOpenAiModel = "gpt-4.1-mini";
    public const string DefaultOpenAiBaseUrl = "https://api.openai.com/v1";
}

public sealed class HazemOptionsValidator : IValidateOptions<HazemOptions>
{
    public ValidateOptionsResult Validate(string? name, HazemOptions o)
    {
        var errors = new List<string>();
        if (o.ApiToken.Length < HazemOptions.MinTokenLength)
        {
            errors.Add($"Hazem:ApiToken (HAZEM_API_TOKEN) must be at least {HazemOptions.MinTokenLength} characters.");
        }

        if (o.N8nInternalSendToken.Length < HazemOptions.MinTokenLength)
        {
            errors.Add($"Hazem:N8nInternalSendToken (N8N_INTERNAL_SEND_TOKEN) must be at least {HazemOptions.MinTokenLength} characters.");
        }

        if (o.ApiToken == o.N8nInternalSendToken)
        {
            errors.Add("HAZEM_API_TOKEN and N8N_INTERNAL_SEND_TOKEN must be different.");
        }

        if (!Uri.TryCreate(o.N8nInternalSendUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            errors.Add("Hazem:N8nInternalSendUrl must be an absolute http(s) URL.");
        }

        if (!Uri.TryCreate(o.OpenAiBaseUrl, UriKind.Absolute, out var ai) || ai.Scheme is not ("http" or "https"))
        {
            errors.Add("Hazem:OpenAiBaseUrl (OPENAI_BASE_URL) must be an absolute http(s) URL.");
        }

        if (string.IsNullOrWhiteSpace(o.OpenAiModel))
        {
            errors.Add("Hazem:OpenAiModel (OPENAI_MODEL) must not be empty.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
