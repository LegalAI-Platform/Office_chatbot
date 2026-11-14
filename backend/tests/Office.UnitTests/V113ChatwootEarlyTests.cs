using Office.Application.Chatwoot;
using Office.Application.Turns;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.113 (the owner, 2026-11-14, of a client's message that showed 8 times in Chatwoot: «هي ظاهره في الشات ووت ان
/// الرساله بتاعتها جت اكتر من مره» … «أيوه، صلّحه»). A client's words go to Chatwoot the moment they are stored (0072), and are
/// taken before they are posted: words that cannot be taken wait for their turn, and words whose post fails go back to their
/// turn. Either way Chatwoot shows them once. That a username client's key can be taken is the database's (migration 0084, the
/// integration tests).
/// </summary>
public sealed class V113ChatwootEarlyTests
{
    private const string Key = "EG.13131313131313131313";
    private static readonly IncomingMessage Words = new("wamid.V113-1", MessageType.Text, "ممكن نحجز معاد؟", null, null);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChatwootMirror Mirror(Api api, Store store) => new(api, store, TimeProvider.System);

    [Fact]
    public async Task Words_that_cannot_be_taken_are_posted_once_with_their_turn_not_every_round()
    {
        // The 2026-11-14 chat: hazem.chatwoot_inbound refused the username client's key, and the words went every round (7)
        var store = new Store { ClaimFails = true };
        var api = new Api();
        store.Stored(Words);

        for (var round = 0; round < 7; round++)
        {
            var mirror = Mirror(api, store);
            Assert.Equal(0, await mirror.SyncAsync(Ct));
            Assert.StartsWith("early: InvalidOperationException: 23514", Assert.Single(mirror.EarlyFailures), StringComparison.Ordinal);
        }

        Assert.Empty(api.Posts);

        store.Answered(Words, "تحب تكون استشارة بالتليفون ولا المقابلة في المكتب؟");
        Assert.Equal(1, await Mirror(api, store).SyncAsync(Ct));
        Assert.Equal([(ChatwootText.Client(Words), ChatwootPost.Incoming), ("تحب تكون استشارة بالتليفون ولا المقابلة في المكتب؟", ChatwootPost.Outgoing)],
            api.Posts);
    }

    [Fact]
    public async Task Words_taken_and_posted_are_never_posted_again_by_a_round_or_their_turn()
    {
        var store = new Store();
        var api = new Api();
        store.Stored(Words);

        Assert.Equal(0, await Mirror(api, store).SyncAsync(Ct));
        Assert.Equal(0, await Mirror(api, store).SyncAsync(Ct));
        store.Answered(Words, "وعليكم السلام");
        Assert.Equal(1, await Mirror(api, store).SyncAsync(Ct));

        Assert.Equal([(ChatwootText.Client(Words), ChatwootPost.Incoming), ("وعليكم السلام", ChatwootPost.Outgoing)], api.Posts);
    }

    [Fact]
    public async Task Words_whose_post_failed_go_back_and_reach_chatwoot_once()
    {
        var store = new Store();
        var api = new Api { Down = true };
        store.Stored(Words);

        var mirror = Mirror(api, store);
        Assert.Equal(0, await mirror.SyncAsync(Ct));
        Assert.StartsWith("early: HttpRequestException", Assert.Single(mirror.EarlyFailures), StringComparison.Ordinal);
        Assert.Empty(store.Taken); // given back

        api.Down = false;
        Assert.Equal(0, await Mirror(api, store).SyncAsync(Ct)); // posted early now
        store.Answered(Words, "وعليكم السلام");
        Assert.Equal(1, await Mirror(api, store).SyncAsync(Ct));

        Assert.Equal([(ChatwootText.Client(Words), ChatwootPost.Incoming), ("وعليكم السلام", ChatwootPost.Outgoing)], api.Posts);
    }

    [Fact]
    public async Task Words_taken_before_are_not_posted()
    {
        var store = new Store { QueryIgnoresTaken = true }; // taken between the query and the claim
        var api = new Api();
        store.Stored(Words);
        Assert.True(await store.ClaimEarlyAsync(Words.Wamid, Key, Ct));

        var mirror = Mirror(api, store);
        Assert.Equal(0, await mirror.SyncAsync(Ct));
        Assert.Empty(api.Posts);
        Assert.Empty(mirror.EarlyFailures);
    }

    [Fact]
    public void A_failure_says_where_and_its_kind_on_one_line_without_the_rows_details()
    {
        var e = new InvalidOperationException("23514: new row violates check constraint \"chatwoot_inbound_wa_id_check\"\n\nDETAIL: Failing row contains (wamid.x, EG.1).");
        Assert.Equal("early: InvalidOperationException: 23514: new row violates check constraint \"chatwoot_inbound_wa_id_check\"",
            ChatwootMirror.Failure("early", e));
    }

    /// <summary>Chatwoot's API at the boundary: what was posted, or down.</summary>
    private sealed class Api : IChatwootApi
    {
        public bool Down { get; set; }

        public List<(string Content, ChatwootPost Kind)> Posts { get; } = [];

        public bool Configured => true;

        public Task<(long ContactId, string SourceId)> FindOrCreateContactAsync(string waId, string? name, CancellationToken ct) =>
            Task.FromResult((1L, "source-1"));

        public Task<long> CreateConversationAsync(long contactId, string sourceId, CancellationToken ct) => Task.FromResult(272L);

        public Task<(int Contacts, int Conversations)> DeleteContactAsync(string waId, CancellationToken ct) => throw new NotSupportedException();

        public Task PostAsync(long conversationId, string content, ChatwootPost kind, CancellationToken ct)
        {
            if (Down)
            {
                throw new HttpRequestException("Response status code does not indicate success: 502 (Bad Gateway).");
            }

            Posts.Add((content, kind));
            return Task.CompletedTask;
        }

        public Task<long> SystemUserIdAsync(CancellationToken ct) => Task.FromResult(1L);

        public Task<long> CreateUserAsync(string name, string email, CancellationToken ct) => throw new NotSupportedException();

        public Task SetRoleAsync(long userId, string? role, CancellationToken ct) => throw new NotSupportedException();

        public Task JoinInboxAsync(long userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<string?> LoginUrlAsync(long userId, CancellationToken ct) => throw new NotSupportedException();

        public string ConversationUrl(long conversationId) => $"https://chat.test/{conversationId}";
    }

    /// <summary>
    /// hazem.chatwoot_* in memory: a stored message is early until its turn is answered; <see cref="ClaimFails"/> is the old
    /// digits-only check refusing the key.
    /// </summary>
    private sealed class Store : IChatwootStore
    {
        private readonly List<IncomingMessage> stored = [];
        private readonly List<PendingTurn> turns = [];
        private readonly HashSet<string> done = new(StringComparer.Ordinal);
        private ChatwootThread? thread;

        public bool ClaimFails { get; init; }

        public bool QueryIgnoresTaken { get; init; }

        public HashSet<string> Taken { get; } = new(StringComparer.Ordinal);

        public void Stored(IncomingMessage m) => stored.Add(m);

        public void Answered(IncomingMessage m, string reply) =>
            turns.Add(new PendingTurn($"wt:v113-{turns.Count}", Key, "عميل", [m],
                [new OutboundAction(ActionType.SendText, $"hazem:v113:{turns.Count}", Text: reply)], 0, 0));

        public Task<ChatwootThread?> FindThreadAsync(string waId, CancellationToken ct) => Task.FromResult(thread);

        public Task<ChatwootThread?> FindThreadByConversationAsync(long conversationId, CancellationToken ct) => Task.FromResult(thread);

        public Task SaveThreadAsync(ChatwootThread t, CancellationToken ct)
        {
            thread = t;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PendingTurn>> PendingTurnsAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PendingTurn>>(turns.Where(t => !done.Contains(t.TurnId))
                .Select(t => t with { Early = t.Messages.Select(m => m.Wamid).Where(Taken.Contains).ToHashSet(StringComparer.Ordinal) })
                .ToList());

        public Task MarkTurnAsync(string turnId, bool ok, int attempts, int posted, DateTimeOffset? nextAt, string? error, CancellationToken ct)
        {
            if (ok)
            {
                done.Add(turnId);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EarlyMessage>> EarlyMessagesAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EarlyMessage>>(stored
                .Where(m => (QueryIgnoresTaken || !Taken.Contains(m.Wamid)) && !turns.Any(t => t.Messages.Any(x => x.Wamid == m.Wamid)))
                .Select(m => new EarlyMessage(Key, "عميل", m)).ToList());

        public Task<bool> ClaimEarlyAsync(string wamid, string waId, CancellationToken ct) =>
            ClaimFails
                ? throw new InvalidOperationException("23514: new row for relation \"chatwoot_inbound\" violates check constraint \"chatwoot_inbound_wa_id_check\"")
                : Task.FromResult(Taken.Add(wamid));

        public Task ReleaseEarlyAsync(string wamid, CancellationToken ct)
        {
            Taken.Remove(wamid);
            return Task.CompletedTask;
        }

        public Task<bool> ClaimReplyAsync(long messageId, long conversationId, string waId, long? userId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task FinishReplyAsync(long messageId, string state, string? error, CancellationToken ct) => throw new NotSupportedException();
    }
}
