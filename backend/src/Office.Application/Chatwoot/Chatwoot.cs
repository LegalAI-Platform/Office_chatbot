using Office.Application.Panel;
using Office.Application.Ports;
using Office.Application.Turns;
using Office.Application.UseCases;

namespace Office.Application.Chatwoot;

// TASK 07b (DECISIONS T-24): Chatwoot is the staff's conversation screen. It is linked to .NET by an
// API inbox and never to WhatsApp: .NET copies in what the client said and what Hazem answered, and
// a staff reply comes back to .NET and goes out through n8n with every guard.

/// <summary>A client's contact and conversation in Chatwoot (hazem.chatwoot_threads).</summary>
public sealed record ChatwootThread(string WaId, long ContactId, string SourceId, long ConversationId);

/// <summary>A turn still to copy (hazem.turns, from chatwoot.mirror_since on).</summary>
/// <summary><paramref name="Posted"/>: how many of its messages reached Chatwoot already (a retry resumes there).</summary>
public sealed record PendingTurn(string TurnId, string WaId, string? ContactName, IReadOnlyList<IncomingMessage> Messages,
    IReadOnlyList<OutboundAction> Actions, int Attempts, int Posted);

/// <summary>Chatwoot's HTTP APIs: the account's (as «حازم (النظام)») and the platform's (accounts of the staff).</summary>
public interface IChatwootApi
{
    /// <summary>The tokens are set (CHATWOOT_BOT_TOKEN, CHATWOOT_PLATFORM_TOKEN).</summary>
    bool Configured { get; }

    Task<(long ContactId, string SourceId)> FindOrCreateContactAsync(string waId, string? name, CancellationToken ct);

    Task<long> CreateConversationAsync(long contactId, string sourceId, CancellationToken ct);

    /// <summary>A message in the conversation: the client's (incoming) or Hazem's (outgoing), or a private note.</summary>
    Task PostAsync(long conversationId, string content, ChatwootPost kind, CancellationToken ct);

    /// <summary>Who «حازم (النظام)» is: what it wrote comes back by webhook and is not sent again.</summary>
    Task<long> SystemUserIdAsync(CancellationToken ct);

    /// <summary>Platform API: a new Chatwoot user for a panel account (confirmed, a random password nobody uses).</summary>
    Task<long> CreateUserAsync(string name, string email, CancellationToken ct);

    /// <summary>Into the office's account with this role, or out of it (null).</summary>
    Task SetRoleAsync(long userId, string? role, CancellationToken ct);

    /// <summary>A member of «واتساب (حازم)»: an agent sees only the inboxes it belongs to.</summary>
    Task JoinInboxAsync(long userId, CancellationToken ct);

    /// <summary>A one-time sign-in link (Chatwoot's SSO): the panel opens Chatwoot without a second password.</summary>
    Task<string?> LoginUrlAsync(long userId, CancellationToken ct);

    string ConversationUrl(long conversationId);
}

public enum ChatwootPost
{
    Incoming,
    Outgoing,
    PrivateNote,
}

public interface IChatwootStore
{
    Task<ChatwootThread?> FindThreadAsync(string waId, CancellationToken ct);

    Task<ChatwootThread?> FindThreadByConversationAsync(long conversationId, CancellationToken ct);

    Task SaveThreadAsync(ChatwootThread thread, CancellationToken ct);

    /// <summary>Turns to copy now, oldest first; a client's turn waits while an earlier one of theirs is not done.</summary>
    Task<IReadOnlyList<PendingTurn>> PendingTurnsAsync(int limit, CancellationToken ct);

    Task MarkTurnAsync(string turnId, bool done, int attempts, int posted, DateTimeOffset? nextAt, string? error, CancellationToken ct);

    /// <summary>False if this message was seen before (a webhook delivered twice).</summary>
    Task<bool> ClaimReplyAsync(long messageId, long conversationId, string waId, long? userId, CancellationToken ct);

    Task FinishReplyAsync(long messageId, string state, string? error, CancellationToken ct);
}

/// <summary>Copies turns into Chatwoot, in order, a few at a time (the background worker calls it).</summary>
public sealed class ChatwootMirror(IChatwootApi api, IChatwootStore store, TimeProvider clock)
{
    public const int Batch = 20;

    /// <summary>After this many tries a turn is left (healthcheck shows it); its client's later turns go on.</summary>
    public const int MaxAttempts = 30;

    /// <summary>How many turns were copied this round.</summary>
    public async Task<int> SyncAsync(CancellationToken ct)
    {
        if (!api.Configured)
        {
            return 0;
        }

        var done = 0;
        var failedClients = new HashSet<string>(StringComparer.Ordinal);
        foreach (var turn in await store.PendingTurnsAsync(Batch, ct))
        {
            // One client's turns stay in order: after a failure, that client's later turns wait.
            if (failedClients.Contains(turn.WaId))
            {
                continue;
            }

            var posted = turn.Posted;
            try
            {
                var thread = await ThreadAsync(turn.WaId, turn.ContactName, ct);
                // Each message once: a retry starts after the ones Chatwoot already has.
                foreach (var (content, kind) in Posts(turn).Skip(turn.Posted))
                {
                    await api.PostAsync(thread.ConversationId, content, kind, ct);
                    posted++;
                }

                await store.MarkTurnAsync(turn.TurnId, true, turn.Attempts + 1, posted, null, null, ct);
                done++;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                failedClients.Add(turn.WaId);
                var attempts = turn.Attempts + 1;
                // 30 s, 1 min, 2 min ... at most an hour between tries; none after MaxAttempts.
                var wait = TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(attempts - 1, 7))));
                DateTimeOffset? next = attempts >= MaxAttempts ? null : clock.GetUtcNow() + wait;
                await store.MarkTurnAsync(turn.TurnId, false, attempts, posted, next, Short($"{e.GetType().Name}: {e.Message}"), ct);
            }
        }

        return done;
    }

    /// <summary>What Chatwoot shows for a turn, in order: the client's messages, then Hazem's replies.</summary>
    public static IEnumerable<(string Content, ChatwootPost Kind)> Posts(PendingTurn turn) =>
        turn.Messages.Select(m => (ChatwootText.Client(m), ChatwootPost.Incoming))
            .Concat(turn.Actions.Select(ChatwootText.Hazem).OfType<string>().Select(t => (t, ChatwootPost.Outgoing)));

    /// <summary>The client's conversation in Chatwoot, made on the first message.</summary>
    public async Task<ChatwootThread> ThreadAsync(string waId, string? name, CancellationToken ct)
    {
        if (await store.FindThreadAsync(waId, ct) is { } known)
        {
            return known;
        }

        var (contactId, sourceId) = await api.FindOrCreateContactAsync(waId, name, ct);
        var conversationId = await api.CreateConversationAsync(contactId, sourceId, ct);
        var thread = new ChatwootThread(waId, contactId, sourceId, conversationId);
        await store.SaveThreadAsync(thread, ct);
        return thread;
    }

    internal static string Short(string s) => s.Length <= 300 ? s : s[..300];
}

/// <summary>
/// What Chatwoot tells the api (webhook): a staff member answered, or resolved the conversation
/// (the owner's choice: resolving hands the client back to Hazem).
/// </summary>
public sealed class ChatwootEvents(IChatwootApi api, IChatwootStore store, SendOutbound send, StaffEvents staff, TimeProvider clock)
{
    public const string RequestedByPrefix = "chatwoot:";

    public enum Result
    {
        Ignored,
        Sent,
        Failed,
        HandedBack,
    }

    public sealed record StaffMessage(long MessageId, long ConversationId, long? SenderUserId, string? Content, bool HasAttachments);

    /// <summary>A staff member's message, sent once to the client through n8n (every guard), then Hazem goes quiet (B-12).</summary>
    public async Task<Result> StaffWroteAsync(StaffMessage m, CancellationToken ct)
    {
        if (m.SenderUserId is null || m.SenderUserId == await api.SystemUserIdAsync(ct))
        {
            return Result.Ignored; // Hazem's own copy, or not a person
        }

        if (await store.FindThreadByConversationAsync(m.ConversationId, ct) is not { } thread
            || !await store.ClaimReplyAsync(m.MessageId, m.ConversationId, thread.WaId, m.SenderUserId, ct))
        {
            return Result.Ignored;
        }

        var text = (m.Content ?? "").Trim();
        if (text.Length == 0)
        {
            await store.FinishReplyAsync(m.MessageId, "skipped", "no_text", ct);
            if (m.HasAttachments)
            {
                await NoteAsync(m.ConversationId, ChatwootText.FilesNotSent, ct);
            }

            return Result.Ignored;
        }

        SendResult result;
        try
        {
            result = await send.SendAsync(new OutboundMessage(ActionType.SendText, thread.WaId, $"chatwoot:{m.MessageId}",
                RequestedByPrefix + m.SenderUserId, Text: text), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // n8n timed out or the result could not be recorded: it may have gone out. Never «try
            // again» here: a second message has a new id, so it would reach the client twice.
            await store.FinishReplyAsync(m.MessageId, "unknown", "send_outcome_unknown", ct);
            await NoteAsync(m.ConversationId, ChatwootText.Unknown, ct);
            return Result.Failed;
        }

        if (!result.Ok)
        {
            await store.FinishReplyAsync(m.MessageId, "failed", ChatwootMirror.Short(result.ErrorCode ?? "send_failed"), ct);
            await NoteAsync(m.ConversationId, ChatwootText.NotSent(result.ErrorCode), ct);
            return Result.Failed;
        }

        await store.FinishReplyAsync(m.MessageId, "sent", null, ct);
        if (m.HasAttachments)
        {
            await NoteAsync(m.ConversationId, ChatwootText.FilesNotSent, ct);
        }

        await staff.ApplyAsync(thread.WaId, StaffEvents.StaffReplied, clock.GetUtcNow(), ct);
        return Result.Sent;
    }

    /// <summary>Resolved in Chatwoot: Hazem answers this client again.</summary>
    public async Task<Result> ResolvedAsync(long conversationId, CancellationToken ct)
    {
        if (await store.FindThreadByConversationAsync(conversationId, ct) is not { } thread)
        {
            return Result.Ignored;
        }

        var (outcome, _, _) = await staff.ApplyAsync(thread.WaId, StaffEvents.HandBack, clock.GetUtcNow(), ct);
        if (outcome != StaffEvents.Outcome.Done)
        {
            return Result.Ignored;
        }

        await NoteAsync(conversationId, ChatwootText.HandedBack, ct);
        return Result.HandedBack;
    }

    private async Task NoteAsync(long conversationId, string text, CancellationToken ct)
    {
        try
        {
            await api.PostAsync(conversationId, text, ChatwootPost.PrivateNote, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ChatwootException)
        {
            // A note is a courtesy: the reply itself is already decided and recorded.
        }
    }
}

/// <summary>
/// The panel's accounts in Chatwoot (T-24, the owner's choice in 1.10): every active approved
/// account is an agent of the office's account and a member of «واتساب (حازم)», and sees every
/// conversation there. Nobody but «حازم (النظام)» is an administrator, so nobody can connect a
/// channel to WhatsApp or move the webhook. A disabled account is taken out. The panel never waits
/// on Chatwoot: a failure is fixed by the next sync (every few minutes).
/// </summary>
public sealed class ChatwootAccounts(IChatwootApi api, IPanelUserStore users)
{
    public const string Agent = "agent";

    public bool Configured => api.Configured;

    public async Task SyncAsync(PanelUser user, CancellationToken ct)
    {
        if (!api.Configured)
        {
            return;
        }

        var active = user is { Active: true, Pending: false };
        if (!active && user.ChatwootUserId is null)
        {
            return;
        }

        var id = user.ChatwootUserId ?? await CreateAsync(user, ct);
        await api.SetRoleAsync(id, active ? Agent : null, ct);
        if (active)
        {
            await api.JoinInboxAsync(id, ct);
        }
    }

    /// <summary>A one-time link that signs this person into Chatwoot (the user is made first if missing).</summary>
    public async Task<string?> LoginUrlAsync(PanelUser user, CancellationToken ct)
    {
        if (!api.Configured || user is not { Active: true, Pending: false })
        {
            return null;
        }

        await SyncAsync(user, ct);
        return await api.LoginUrlAsync(user.ChatwootUserId!.Value, ct);
    }

    /// <summary>Every account, each on its own: one that fails does not stop the others. Returns what failed.</summary>
    public async Task<IReadOnlyList<string>> SyncAllAsync(CancellationToken ct)
    {
        var failed = new List<string>();
        foreach (var user in await users.ListAsync(ct))
        {
            try
            {
                await SyncAsync(user, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                failed.Add($"{user.Id}: {e.GetType().Name}: {ChatwootMirror.Short(e.Message)}");
            }
        }

        return failed;
    }

    private async Task<long> CreateAsync(PanelUser user, CancellationToken ct)
    {
        var id = await api.CreateUserAsync(user.DisplayName, user.Email, ct);
        // Chatwoot hands back the user that has this e-mail already, if any: never link an account
        // to the api's own user.
        if (id == await api.SystemUserIdAsync(ct))
        {
            throw new ChatwootException("this e-mail belongs to Chatwoot's system user");
        }

        user.ChatwootUserId = id;
        await users.SetChatwootUserIdAsync(user.Id, id, ct);
        return id;
    }
}

/// <summary>Chatwoot answered, but not with what was asked for.</summary>
public sealed class ChatwootException(string message) : Exception(message);

/// <summary>
/// The words shown in Chatwoot. A file never goes there (the owner's decision): only its kind,
/// and that it opens from the office panel. Captions and voice transcripts are the client's words.
/// </summary>
public static class ChatwootText
{
    public const string FilesNotSent = "⚠️ الملفات والصور من Chatwoot مش بتتبعت للعميل. ابعت كلام بس.";
    public const string HandedBack = "🤖 المحادثة رجعت لحازم: هيرد على العميل من الرسالة الجاية.";
    public const string Unknown = "⚠️ مش متأكدين إن الرد ده وصل للعميل (السيستم ما ردّش في الوقت). اسأل مدير المكتب يتأكد من السجل قبل ما تبعته تاني، علشان ما يوصلش مرتين.";

    /// <summary>Why a reply did not go out, in the staff's words (the code stays for the record).</summary>
    public static string NotSent(string? code) => "⚠️ الرد ده ما اتبعتش للعميل: " + (code switch
    {
        "OUTSIDE_24H_WINDOW" => "العميل ما بعتش رسالة في آخر 24 ساعة، وواتساب مش بيسمح برد عادي غير لو هو بعت الأول.",
        "NOT_TEST_RECIPIENT" or "RECIPIENT_NOT_ALLOWED" => "الرقم ده مش من أرقام التجربة المسموح الرد عليها دلوقتي.",
        "RATE_LIMIT" => "واتساب طالب نستنى شوية. جرّب تاني بعد دقيقة.",
        "TOKEN_INVALID" => "ربط واتساب محتاج يتظبط. كلّم مدير المكتب.",
        "n8n_unreachable" or "NETWORK_OR_UNKNOWN" or "API_UNAVAILABLE" => "السيستم ما قدرش يوصل لواتساب دلوقتي. جرّب تاني بعد شوية.",
        _ => "حصلت مشكلة. جرّب تاني، أو كلّم مدير المكتب.",
    }) + $" ({code ?? "error"})";

    public static string Client(IncomingMessage m)
    {
        var note = m.Type switch
        {
            MessageType.Text => null,
            MessageType.Image => "📷 صورة",
            MessageType.Video => "🎬 فيديو",
            MessageType.Document => "📄 ملف",
            MessageType.Sticker => "🙂 ستيكر",
            MessageType.Audio => m.Media?.Voice == false ? "🎵 ملف صوتي" : "🎤 رسالة صوتية",
            MessageType.Location => "📍 موقع",
            MessageType.Reaction => $"تفاعل {m.Reaction?.Emoji}".Trim(),
            MessageType.Interactive => m.Interactive?.Title is { } t ? $"اختار: {t}" : "اختيار من قايمة",
            MessageType.Button => m.Button?.Text is { } b ? $"دوس: {b}" : "دوس على زرار",
            _ => "رسالة مش مدعومة",
        };
        var parts = new List<string>();
        if (note is not null)
        {
            parts.Add(m.IsMedia ? $"[{note} — بتتفتح من لوحة المكتب]" : $"[{note}]");
        }

        if (!string.IsNullOrWhiteSpace(m.Text))
        {
            parts.Add(m.Text.Trim());
        }

        if (!string.IsNullOrWhiteSpace(m.Caption))
        {
            parts.Add(m.Caption.Trim());
        }

        if (m.Transcript is { Status: Transcript.Done, Text: { Length: > 0 } said })
        {
            parts.Add($"«{said.Trim()}»");
        }

        if (m.Location is { Name: { Length: > 0 } place })
        {
            parts.Add(place);
        }

        return parts.Count == 0 ? "[رسالة فاضية]" : string.Join("\n", parts);
    }

    /// <summary>What Hazem said, as sent (null for nothing to show).</summary>
    public static string? Hazem(OutboundAction a) => a.Type switch
    {
        ActionType.SendText when !string.IsNullOrWhiteSpace(a.Text) => a.Text,
        ActionType.SendTemplate => $"[قالب واتساب: {a.TemplateName}]",
        _ => null,
    };
}
