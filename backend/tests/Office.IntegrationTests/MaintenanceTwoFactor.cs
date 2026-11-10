using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Office.Infrastructure.Panel;

namespace Office.IntegrationTests;

/// <summary>
/// v1.100: a maintenance account opens nothing until its authenticator is on (the owner: its user name and password alone
/// never open it). A client signed in as one turns it on here, as the panel's «شغّل التحقق بخطوتين» does.
/// </summary>
internal static class MaintenanceTwoFactor
{
    public static async Task TurnOnAsync(HttpClient c, CancellationToken ct, TimeProvider? clock = null)
    {
        var setup = await c.PostAsJsonAsync("/api/panel/auth/2fa/setup", new { }, ct);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var secret = (string)(await setup.Content.ReadFromJsonAsync<JsonNode>(ct))!["secret"]!;
        var step = (clock ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds() / 30 - 1;
        var on = await c.PostAsJsonAsync("/api/panel/auth/2fa/enable", new { code = PanelCrypto.Totp(PanelCrypto.FromBase32(secret), step) }, ct);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
    }
}
