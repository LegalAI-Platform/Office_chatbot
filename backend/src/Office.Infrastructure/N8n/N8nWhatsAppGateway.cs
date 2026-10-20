using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Office.Core.Hazem;

namespace Office.Infrastructure.N8n;

/// <summary>
/// Calls n8n's internal send webhook (workflow WA-INTERNAL-SEND). n8n applies the 24-hour
/// window, template approval and idempotency rules, then talks to Meta.
/// </summary>
public sealed class N8nWhatsAppGateway(HttpClient http, IOptions<HazemOptions> options) : IWhatsAppGateway
{
    public const string HttpClientName = "n8n";
    public const string TokenHeader = "X-Hazem-Token";

    public async Task<SendResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.N8nInternalSendUrl)
        {
            Content = JsonContent.Create(message, options: HazemJson.Options),
        };
        request.Headers.Add(TokenHeader, options.Value.N8nInternalSendToken);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new SendResult(message.IdempotencyKey, false, Status: "failed",
                ErrorCode: $"N8N_HTTP_{(int)response.StatusCode}", ErrorMessage: "n8n refused or failed the request");
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<SendResult>(HazemJson.Options, ct);
            return (result ?? new SendResult(null, false, ErrorCode: "N8N_EMPTY_RESPONSE")) with { IdempotencyKey = message.IdempotencyKey };
        }
        catch (JsonException)
        {
            return new SendResult(message.IdempotencyKey, false, Status: "failed", ErrorCode: "N8N_BAD_RESPONSE");
        }
    }
}
