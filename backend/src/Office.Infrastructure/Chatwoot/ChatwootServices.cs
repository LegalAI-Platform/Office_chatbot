using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Office.Application.Chatwoot;
using Office.Application.Turns;
using Office.Infrastructure.Json;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Chatwoot;

/// <summary>Chatwoot__* (TASK 07b). The tokens come from .env (CHATWOOT_*); chatwoot-setup puts them into Chatwoot.</summary>
public sealed class ChatwootOptions
{
    public const string Section = "Chatwoot";

    /// <summary>How the api reaches Chatwoot on office_net.</summary>
    public string BaseUrl { get; set; } = "http://chatwoot:3000";

    /// <summary>How people open Chatwoot (CHATWOOT_URL): links from the panel.</summary>
    public string PublicUrl { get; set; } = "http://127.0.0.1:3000";

    /// <summary>CHATWOOT_BOT_TOKEN: «حازم (النظام)», an administrator of the office's account.</summary>
    public string BotToken { get; set; } = "";

    /// <summary>CHATWOOT_PLATFORM_TOKEN: the Platform App that manages the staff's Chatwoot users.</summary>
    public string PlatformToken { get; set; } = "";

    /// <summary>CHATWOOT_WEBHOOK_TOKEN: the inbox's webhook secret (X-Chatwoot-Signature).</summary>
    public string WebhookSecret { get; set; } = "";

    public string AccountName { get; set; } = "مكتب سعد فتحي سعد";

    public string InboxName { get; set; } = "واتساب (حازم)";

    /// <summary>Seconds between two rounds of copying turns.</summary>
    public int PollSeconds { get; set; } = 3;

    /// <summary>False in tests: they run the rounds themselves.</summary>
    public bool RunWorker { get; set; } = true;

    public bool Enabled => BotToken.Length > 0 && PlatformToken.Length > 0;
}

/// <summary>
/// Chatwoot's account API (as «حازم (النظام)») and platform API. The account and inbox are found by
/// name once (chatwoot-setup made them) and kept. Never logs a token or a message.
/// </summary>
public sealed class ChatwootApi(HttpClient http, IOptions<ChatwootOptions> options) : IChatwootApi
{
    public const string HttpClientName = "chatwoot";
    private const string TokenHeader = "api_access_token";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (long Account, long Inbox, long User)? _ids;

    private ChatwootOptions O => options.Value;

    public bool Configured => O.Enabled;

    public async Task<(long ContactId, string SourceId)> FindOrCreateContactAsync(string waId, string? name, CancellationToken ct)
    {
        var (account, inbox, _) = await IdsAsync(ct);
        var found = await SendAsync(HttpMethod.Get, $"/api/v1/accounts/{account}/contacts/search?q={waId}", O.BotToken, null, ct);
        foreach (var c in found["payload"]?.AsArray() ?? [])
        {
            if ((string?)c?["identifier"] != waId)
            {
                continue;
            }

            var contactId = (long)c!["id"]!;
            var source = c["contact_inboxes"]?.AsArray().FirstOrDefault(ci => (long?)ci?["inbox"]?["id"] == inbox)?["source_id"];
            if (source is not null)
            {
                return (contactId, (string)source!);
            }

            var linked = await SendAsync(HttpMethod.Post, $"/api/v1/accounts/{account}/contacts/{contactId}/contact_inboxes",
                O.BotToken, new { inbox_id = inbox }, ct);
            return (contactId, (string)linked["source_id"]!);
        }

        // v1.37 (TASK 15): a client with a WhatsApp username has a BSUID, not a number: the identifier only.
        var phone = Office.Domain.Common.WaId.IsPhoneNumber(waId) ? $"+{waId}" : null;
        var made = await SendAsync(HttpMethod.Post, $"/api/v1/accounts/{account}/contacts", O.BotToken, new
        {
            inbox_id = inbox,
            name = string.IsNullOrWhiteSpace(name) ? phone ?? Office.Application.Booking.UrgentTexts.NoNumber : name.Trim(),
            phone_number = phone,
            identifier = waId,
        }, ct);
        var contact = made["payload"]?["contact"] ?? made;
        var sourceId = contact["contact_inboxes"]?.AsArray().FirstOrDefault()?["source_id"]
            ?? throw new ChatwootException("contact made without an inbox");
        return ((long)contact["id"]!, (string)sourceId!);
    }

    public async Task<long> CreateConversationAsync(long contactId, string sourceId, CancellationToken ct)
    {
        var (account, inbox, _) = await IdsAsync(ct);
        var c = await SendAsync(HttpMethod.Post, $"/api/v1/accounts/{account}/conversations", O.BotToken,
            new { source_id = sourceId, inbox_id = inbox, contact_id = contactId }, ct);
        return (long)c["id"]!;
    }

    public async Task<(int Contacts, int Conversations)> DeleteContactAsync(string waId, CancellationToken ct)
    {
        var (account, _, _) = await IdsAsync(ct);
        var found = await SendAsync(HttpMethod.Get, $"/api/v1/accounts/{account}/contacts/search?q={waId}", O.BotToken, null, ct);
        int contacts = 0, conversations = 0;
        foreach (var c in found["payload"]?.AsArray() ?? [])
        {
            if ((string?)c?["identifier"] != waId && (string?)c?["phone_number"] != "+" + waId)
            {
                continue;
            }

            var contactId = (long)c!["id"]!;
            var convs = await SendAsync(HttpMethod.Get, $"/api/v1/accounts/{account}/contacts/{contactId}/conversations", O.BotToken, null, ct,
                allowNotFound: true);
            foreach (var conv in convs["payload"]?.AsArray() ?? [])
            {
                await SendAsync(HttpMethod.Delete, $"/api/v1/accounts/{account}/conversations/{(long)conv!["id"]!}", O.BotToken, null, ct,
                    allowNotFound: true);
                conversations++;
            }

            await SendAsync(HttpMethod.Delete, $"/api/v1/accounts/{account}/contacts/{contactId}", O.BotToken, null, ct, allowNotFound: true);
            contacts++;
        }

        return (contacts, conversations);
    }

    public async Task PostAsync(long conversationId, string content, ChatwootPost kind, CancellationToken ct)
    {
        var (account, _, _) = await IdsAsync(ct);
        await SendAsync(HttpMethod.Post, $"/api/v1/accounts/{account}/conversations/{conversationId}/messages", O.BotToken, new
        {
            content,
            message_type = kind == ChatwootPost.Incoming ? "incoming" : "outgoing",
            @private = kind == ChatwootPost.PrivateNote,
        }, ct);
    }

    public async Task<long> SystemUserIdAsync(CancellationToken ct) => (await IdsAsync(ct)).User;

    public async Task<long> CreateUserAsync(string name, string email, CancellationToken ct)
    {
        // Nobody types this password: the panel signs people in with a one-time link.
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "aA1!";
        var u = await SendAsync(HttpMethod.Post, "/platform/api/v1/users", O.PlatformToken,
            new { name, display_name = name, email, password }, ct);
        return (long)u["id"]!;
    }

    public async Task SetRoleAsync(long userId, string? role, CancellationToken ct)
    {
        var (account, _, _) = await IdsAsync(ct);
        if (role is null)
        {
            await SendAsync(HttpMethod.Delete, $"/platform/api/v1/accounts/{account}/account_users", O.PlatformToken,
                new { user_id = userId }, ct, allowNotFound: true);
            return;
        }

        await SendAsync(HttpMethod.Post, $"/platform/api/v1/accounts/{account}/account_users", O.PlatformToken,
            new { user_id = userId, role }, ct);
    }

    public async Task JoinInboxAsync(long userId, CancellationToken ct)
    {
        var (account, inbox, _) = await IdsAsync(ct);
        // Adds only (Chatwoot's create never removes other members).
        await SendAsync(HttpMethod.Post, $"/api/v1/accounts/{account}/inbox_members", O.BotToken,
            new { inbox_id = inbox, user_ids = new[] { userId } }, ct);
    }

    public async Task<string?> LoginUrlAsync(long userId, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Get, $"/platform/api/v1/users/{userId}/login", O.PlatformToken, null, ct);
        // The link names Chatwoot's internal address; people open the public one.
        return (string?)r["url"] is { } url ? Public(url) : null;
    }

    public string ConversationUrl(long conversationId) =>
        $"{O.PublicUrl.TrimEnd('/')}/app/accounts/{_ids?.Account ?? 0}/conversations/{conversationId}";

    private string Public(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? O.PublicUrl.TrimEnd('/') + u.PathAndQuery : url;

    /// <summary>The office's account, its inbox and «حازم (النظام)», by name.</summary>
    private async Task<(long Account, long Inbox, long User)> IdsAsync(CancellationToken ct)
    {
        if (_ids is { } known)
        {
            return known;
        }

        await Gate.WaitAsync(ct);
        try
        {
            if (_ids is { } again)
            {
                return again;
            }

            var profile = await SendAsync(HttpMethod.Get, "/api/v1/profile", O.BotToken, null, ct);
            var account = profile["accounts"]?.AsArray().FirstOrDefault(a => (string?)a?["name"] == O.AccountName)?["id"]
                ?? throw new ChatwootException("the office's account is missing: run chatwoot-setup");
            var inboxes = await SendAsync(HttpMethod.Get, $"/api/v1/accounts/{(long)account}/inboxes", O.BotToken, null, ct);
            var inbox = inboxes["payload"]?.AsArray().FirstOrDefault(i => (string?)i?["name"] == O.InboxName)?["id"]
                ?? throw new ChatwootException("the inbox is missing: run chatwoot-setup");
            _ids = ((long)account, (long)inbox, (long)profile["id"]!);
            return _ids.Value;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>For tests: the ids found by name are per Chatwoot, not per process.</summary>
    public static void Forget() => _ids = null;

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, string token, object? body, CancellationToken ct,
        bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, O.BaseUrl.TrimEnd('/') + path);
        request.Headers.Add(TokenHeader, token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, ct);
        if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new JsonObject();
        }

        if (!response.IsSuccessStatusCode)
        {
            // The status and path only: an error body may repeat the message or the contact.
            throw new ChatwootException($"{method} {path.Split('?')[0]} -> {(int)response.StatusCode}");
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        return text.Length == 0 ? new JsonObject() : JsonNode.Parse(text) ?? new JsonObject();
    }
}

/// <summary>hazem.chatwoot_threads, chatwoot_turns and chatwoot_replies (migration 0011).</summary>
public sealed class ChatwootStore(HazemDbContext db, TimeProvider clock) : IChatwootStore
{
    public const string MirrorSinceKey = "chatwoot.mirror_since";

    public async Task<ChatwootThread?> FindThreadAsync(string waId, CancellationToken ct) =>
        await db.ChatwootThreads.AsNoTracking().Where(t => t.WaId == waId)
            .Select(t => new ChatwootThread(t.WaId, t.ContactId, t.SourceId, t.ConversationId)).FirstOrDefaultAsync(ct);

    public async Task<ChatwootThread?> FindThreadByConversationAsync(long conversationId, CancellationToken ct) =>
        await db.ChatwootThreads.AsNoTracking().Where(t => t.ConversationId == conversationId)
            .Select(t => new ChatwootThread(t.WaId, t.ContactId, t.SourceId, t.ConversationId)).FirstOrDefaultAsync(ct);

    public Task SaveThreadAsync(ChatwootThread t, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO hazem.chatwoot_threads (wa_id, contact_id, source_id, conversation_id)
        VALUES ({t.WaId}, {t.ContactId}, {t.SourceId}, {t.ConversationId})
        ON CONFLICT (wa_id) DO UPDATE SET contact_id = EXCLUDED.contact_id, source_id = EXCLUDED.source_id,
                                          conversation_id = EXCLUDED.conversation_id
        """, ct);

    public async Task<IReadOnlyList<PendingTurn>> PendingTurnsAsync(int limit, CancellationToken ct)
    {
        var setting = await db.Settings.AsNoTracking().Where(s => s.Key == MirrorSinceKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (!DateTimeOffset.TryParse(setting, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var since))
        {
            return [];
        }

        var now = clock.GetUtcNow();
        // Not done yet: no row, or failed. A turn waits while an earlier turn of the same client is
        // not done (unless that one was given up: no next try), so Chatwoot keeps the client's order.
        var rows = await (from t in db.Turns.AsNoTracking()
                          join s in db.ChatwootTurns.AsNoTracking() on t.TurnId equals s.TurnId into ss
                          from s in ss.DefaultIfEmpty()
                          where t.CreatedAt >= since && t.Status == TurnStore.Processed
                                && (s == null || (s.State == "failed" && s.NextAt <= now))
                                && !(from e in db.Turns
                                     join es in db.ChatwootTurns on e.TurnId equals es.TurnId into ess
                                     from es in ess.DefaultIfEmpty()
                                     where e.WaId == t.WaId && e.CreatedAt >= since && e.CreatedAt < t.CreatedAt
                                           && e.Status == TurnStore.Processed
                                           && (es == null || (es.State == "failed" && es.NextAt != null))
                                     select e.Id).Any()
                          orderby t.CreatedAt
                          select new { t, Attempts = s == null ? 0 : s.Attempts, Posted = s == null ? 0 : s.Posted })
            .Take(limit).ToListAsync(ct);
        return rows.Select(r => new PendingTurn(r.t.TurnId, r.t.WaId, r.t.ContactName,
            JsonSerializer.Deserialize<List<IncomingMessage>>(r.t.Messages, HazemJson.Options) ?? [],
            JsonSerializer.Deserialize<List<OutboundAction>>(r.t.Actions, HazemJson.Options) ?? [],
            r.Attempts, r.Posted)).ToList();
    }

    public Task MarkTurnAsync(string turnId, bool done, int attempts, int posted, DateTimeOffset? nextAt, string? error, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.chatwoot_turns (turn_id, state, attempts, posted, next_at, last_error, updated_at)
            VALUES ({turnId}, {(done ? "done" : "failed")}, {Math.Min(attempts, 1000)}, {posted}, {nextAt?.ToUniversalTime()}, {error}, {clock.GetUtcNow()})
            ON CONFLICT (turn_id) DO UPDATE SET state = EXCLUDED.state, attempts = EXCLUDED.attempts, posted = EXCLUDED.posted,
                                                next_at = EXCLUDED.next_at, last_error = EXCLUDED.last_error, updated_at = EXCLUDED.updated_at
            """, ct);

    public async Task<bool> ClaimReplyAsync(long messageId, long conversationId, string waId, long? userId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.chatwoot_replies (message_id, conversation_id, wa_id, user_id, state)
            VALUES ({messageId}, {conversationId}, {waId}, {userId}, 'sending')
            ON CONFLICT (message_id) DO NOTHING
            """, ct) == 1;

    public Task FinishReplyAsync(long messageId, string state, string? error, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.chatwoot_replies SET state = {state}, error = {error}, updated_at = {clock.GetUtcNow()}
             WHERE message_id = {messageId}
            """, ct);
}

/// <summary>
/// Copies new turns into Chatwoot every few seconds, and once at start makes sure every panel
/// account has its Chatwoot user. Off when the Chatwoot tokens are not set (tests, a bare install).
/// </summary>
public sealed class ChatwootWorker(IServiceScopeFactory scopes, IOptions<ChatwootOptions> options, ILogger<ChatwootWorker> log)
    : BackgroundService
{
    public static readonly TimeSpan AccountsEvery = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!options.Value.RunWorker)
        {
            return;
        }

        if (!options.Value.Enabled)
        {
            log.LogInformation("Chatwoot link off: CHATWOOT_BOT_TOKEN or CHATWOOT_PLATFORM_TOKEN is empty");
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds));
        var nextAccounts = DateTimeOffset.MinValue;
        while (!stop.IsCancellationRequested)
        {
            // Accounts on their own, every ten minutes: a disabled account leaves Chatwoot even if
            // Chatwoot was down when the manager disabled it; one failing account blocks nothing.
            if (DateTimeOffset.UtcNow >= nextAccounts)
            {
                nextAccounts = DateTimeOffset.UtcNow + AccountsEvery;
                try
                {
                    using var scope = scopes.CreateScope();
                    foreach (var failure in await scope.ServiceProvider.GetRequiredService<ChatwootAccounts>().SyncAllAsync(stop))
                    {
                        log.LogWarning("Chatwoot account sync failed: {Failure}", failure);
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning("Chatwoot account sync failed: {Kind}: {Message}", e.GetType().Name, e.Message);
                }
            }

            try
            {
                using var scope = scopes.CreateScope();
                var copied = await scope.ServiceProvider.GetRequiredService<ChatwootMirror>().SyncAsync(stop);
                if (copied > 0)
                {
                    log.LogInformation("Chatwoot: {Count} turn(s) copied", copied);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Chatwoot round failed: {Kind}: {Message}", e.GetType().Name, e.Message);
            }

            try
            {
                await Task.Delay(delay, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
