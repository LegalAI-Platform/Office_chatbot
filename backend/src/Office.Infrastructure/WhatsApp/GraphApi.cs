using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Office.Core.WhatsApp;

namespace Office.Infrastructure.WhatsApp;

/// <summary>Raw result of a Graph API call. Never contains the access token.</summary>
public sealed record GraphResponse(HttpStatusCode? Status, JsonNode? Body, string? RawBody, string? TransportError)
{
    public bool Ok => Status is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices;
    public int? MetaCode => Body?["error"]?["code"]?.GetValue<int>();
    public string? MetaMessage => Body?["error"]?["message"]?.GetValue<string>() ?? TransportError;
}

/// <summary>
/// Thin Graph API client. The message-sending call is retried only on 429/502/503/504 and
/// connection failures (see DependencyInjection) so a timeout never sends a message twice.
/// </summary>
public sealed class GraphApiClient(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<GraphApiClient> logger)
{
    public const string HttpClientName = "graph-api";

    private WhatsAppOptions O => options.Value;

    public Task<GraphResponse> GetAsync(string path, bool appToken = false, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, path, null, appToken, ct);

    public Task<GraphResponse> PostJsonAsync(string path, JsonNode body, bool appToken = false, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, path, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), appToken, ct);

    public Task<GraphResponse> PostFormAsync(string path, IDictionary<string, string> form, bool appToken = false, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, path, new FormUrlEncodedContent(form), appToken, ct);

    private async Task<GraphResponse> SendAsync(HttpMethod method, string path, HttpContent? content, bool appToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{O.GraphApiVersion}/{path.TrimStart('/')}") { Content = content };
        // App access token (app_id|app_secret) for app-level calls such as webhook subscriptions;
        // the system user token for everything else.
        var token = appToken ? $"{O.AppId}|{O.AppSecret}" : O.AccessToken;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            JsonNode? body = null;
            try
            {
                body = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
            }
            catch (System.Text.Json.JsonException)
            {
                // Non-JSON error page; keep the raw text only.
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Graph API {Method} {Path} failed with HTTP {Status}, Meta code {MetaCode}",
                    method.Method, path.Split('?')[0], (int)response.StatusCode, body?["error"]?["code"]?.ToString());
            }

            return new GraphResponse(response.StatusCode, body, raw, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Graph API {Method} {Path} transport failure: {Error}", method.Method, path.Split('?')[0], ex.GetType().Name);
            return new GraphResponse(null, null, null, ex.GetType().Name);
        }
    }
}

/// <summary>Sends messages for the Core <see cref="MessageSender"/>.</summary>
public sealed class WhatsAppCloudApi(GraphApiClient graph, IOptions<WhatsAppOptions> options) : IWhatsAppCloudApi
{
    public async Task<CloudApiResult> SendMessageAsync(string requestJson, CancellationToken ct)
    {
        var response = await graph.PostJsonAsync($"{options.Value.PhoneNumberId}/messages", JsonNode.Parse(requestJson)!, ct: ct);
        if (response.Ok)
        {
            var wamid = response.Body?["messages"]?[0]?["id"]?.GetValue<string>();
            return new CloudApiResult(wamid is not null, wamid, response.RawBody, null, wamid is null ? "No message id in response" : null, null);
        }

        return new CloudApiResult(false, null, response.RawBody, response.MetaCode, response.MetaMessage, response.Body?["error"]?.ToJsonString());
    }
}
