using Office.Infrastructure.Monitoring;

namespace Office.Api;

/// <summary>
/// v1.107 (the monitoring): which of .NET's own meters the API hands to Prometheus, on <see cref="DefaultPort"/>. Each one
/// names the route template, the status, the host it called or the pool, and never anything a client sent.
/// </summary>
public static class OfficeMetrics
{
    /// <summary>The port of /metrics (Metrics:Port): inside the Docker networks only, never published, never through nginx.</summary>
    public const int DefaultPort = 9464;

    public static readonly string[] Meters =
    [
        "Microsoft.AspNetCore.Hosting", // http.server.request.duration, http.server.active_requests
        "Microsoft.AspNetCore.Server.Kestrel", // open connections, connections waiting
        "Microsoft.AspNetCore.Diagnostics", // exceptions the API did not handle
        "Microsoft.AspNetCore.RateLimiting", // sign-ins refused by the limit
        "Microsoft.AspNetCore.Authentication", // cookie sign-ins and challenges (.NET 10)
        "Microsoft.AspNetCore.Authorization", // permission checks (.NET 10)
        "System.Runtime", // GC, memory, CPU, threads, the thread pool, exceptions (.NET 9+)
        "System.Net.Http", // the calls out: OpenAI, Anthropic, Chatwoot, n8n, Google (by host, never the path)
        "Npgsql", // the database connection pool and its commands
        AlertMetrics.MeterName, // the alerts' rounds and e-mails
    ];
}
