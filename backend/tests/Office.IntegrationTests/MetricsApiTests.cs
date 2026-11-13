using Microsoft.AspNetCore.TestHost;

namespace Office.IntegrationTests;

/// <summary>
/// v1.107 (the monitoring): the API's numbers answer on their own port (9464) and nowhere else, carry the requests, the .NET
/// runtime and the database pool, and hold no secret and nothing a client sent.
/// </summary>
[Collection(nameof(MetricsApiTests))]
public sealed class MetricsApiTests(TestDatabase db) : IAsyncLifetime
{
    private const int MetricsPort = 9464;
    private const string MadeUpPhone = "01012345678"; // a made-up number (.gitleaks.toml)
    private ApiFactory _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(db);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>A request as if it came in on <paramref name="port"/> (the test server has no real sockets).</summary>
    private async Task<(int Status, string Body)> On(int port, string path, string method = "GET")
    {
        var ctx = await _factory.Server.SendAsync(c =>
        {
            c.Request.Method = method;
            c.Request.Path = path;
            c.Connection.LocalPort = port;
        }, Ct);
        using var reader = new StreamReader(ctx.Response.Body);
        return (ctx.Response.StatusCode, await reader.ReadToEndAsync(Ct));
    }

    [Fact]
    public async Task The_metrics_on_9464_carry_the_requests_the_runtime_and_the_database_pool_and_no_secret()
    {
        using (var client = _factory.CreateClient())
        {
            // the database (the readiness check), a counted request, and one with a client's number in its query
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/health/ready", Ct)).StatusCode);
            await client.GetAsync("/api/panel/auth/me", Ct);
            await client.GetAsync($"/api/panel/clients?q={MadeUpPhone}", Ct);
        }

        var (status, body) = await On(MetricsPort, "/metrics");
        Assert.Equal(200, status);
        Assert.Contains("http_server_request_duration_seconds_bucket", body, StringComparison.Ordinal);
        Assert.Contains("http_route=\"/api/panel/auth/me\"", body, StringComparison.Ordinal);
        Assert.Contains("dotnet_", body, StringComparison.Ordinal); // System.Runtime
        Assert.Contains("db_client_", body, StringComparison.Ordinal); // Npgsql's pool
        Assert.DoesNotContain("/health/", body, StringComparison.Ordinal); // Docker's and the monitoring's checks are not counted

        // never a secret, never what a client sent
        Assert.DoesNotContain(Environment.GetEnvironmentVariable("OFFICE_API_PASSWORD")!, body, StringComparison.Ordinal);
        Assert.DoesNotContain("password=", body, StringComparison.OrdinalIgnoreCase); // the pool's name is its connection string
        Assert.DoesNotContain(ApiFactory.ApiToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiFactory.N8nToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(MadeUpPhone, body, StringComparison.Ordinal);
        Assert.DoesNotContain("q=", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_metrics_are_never_on_the_api_port_and_the_metrics_port_answers_nothing_else()
    {
        using (var client = _factory.CreateClient())
        {
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/metrics", Ct)).StatusCode);
        }

        Assert.Equal(404, (await On(MetricsPort, "/health/live")).Status);
        Assert.Equal(404, (await On(MetricsPort, "/api/hazem/turns", "POST")).Status);
        Assert.Equal(404, (await On(MetricsPort, "/metrics", "POST")).Status);
        Assert.Equal(200, (await On(MetricsPort, "/metrics")).Status);
    }
}
