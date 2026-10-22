using System.Collections.Concurrent;
using Office.Api.Contracts;
using Office.Application.Ports;
using Office.Application.Responders;
using Office.Application.Turns;
using Office.Application.UseCases;

namespace Office.UnitTests;

public sealed class HazemRulesTests
{
    private const string Recipient = "201000000041";

    private static TurnRequest Turn(string turnId = "wa:wamid.1", string waId = Recipient, string? text = "مرحبا") =>
        new(turnId, waId, "Client", [new TurnMessage("wamid.1", "text", text, DateTimeOffset.Parse("2026-10-20T10:00:00+03:00"), null)]);

    private static HazemSettings Settings(bool echo, string recipients = Recipient) => new(new Dictionary<string, string>
    {
        [HazemSettings.EchoEnabledKey] = echo ? "true" : "false",
        [HazemSettings.EchoRecipientsKey] = recipients,
    });

    [Fact]
    public void Valid_turn_has_no_errors() => Assert.Empty(TurnRules.Validate(Turn()));

    [Theory]
    [InlineData("", Recipient, "turn_id")]
    [InlineData("wa:1", "12ab", "wa_id")]
    [InlineData("wa:1", "2010", "wa_id")]
    [InlineData("wa:1", "2010000000412345678", "wa_id")]
    public void Invalid_turn_fields_are_reported(string turnId, string waId, string field) =>
        Assert.Contains(field, TurnRules.Validate(Turn(turnId, waId)).Keys);

    [Fact]
    public void Turn_without_messages_is_invalid() =>
        Assert.Contains("messages", TurnRules.Validate(new TurnRequest("wa:1", Recipient, null, [])).Keys);

    [Fact]
    public void Text_over_4096_characters_is_invalid() =>
        Assert.Contains("messages[0].text", TurnRules.Validate(Turn(text: new string('x', 4097))).Keys);

    [Fact]
    public void Turn_id_over_200_characters_is_invalid() =>
        Assert.Contains("turn_id", TurnRules.Validate(Turn(turnId: new string('t', 201))).Keys);

    [Fact]
    public void Send_results_need_an_idempotency_key() =>
        Assert.Contains("results[0].idempotency_key", TurnRules.Validate(new SendResultsRequest(null, [new SendResult(null, true)])).Keys);

    [Fact]
    public void Echo_is_off_by_default()
    {
        var (responder, actions, _, _) = EchoResponder.Decide((Turn()).ToTurn(), new HazemSettings(new Dictionary<string, string>()));
        Assert.Equal(TurnDecision.None, responder);
        Assert.Empty(actions);
    }

    [Fact]
    public void Echo_answers_a_listed_number_once_with_a_stable_key()
    {
        var (responder, actions, _, _) = EchoResponder.Decide((Turn()).ToTurn(), Settings(echo: true, recipients: $"201000000099, {Recipient}"));
        Assert.Equal(EchoResponder.ResponderName, responder);
        var action = Assert.Single(actions);
        Assert.Equal(ActionType.SendText, action.Type);
        Assert.Equal(EchoResponder.EchoText, action.Text);
        Assert.Equal("hazem:wa:wamid.1:0", action.IdempotencyKey);
        Assert.Equal("wamid.1", action.ContextWamid);
    }

    [Fact]
    public void Echo_never_answers_a_number_that_is_not_listed() =>
        Assert.Empty(EchoResponder.Decide((Turn(waId: "201000000077")).ToTurn(), Settings(echo: true)).Actions);

    [Fact]
    public void Echo_ignores_turns_without_text() =>
        Assert.Empty(EchoResponder.Decide((Turn(text: null)).ToTurn(), Settings(echo: true)).Actions);

    [Fact]
    public void Phone_numbers_are_masked_in_logs() => Assert.Equal("*********041", TurnRules.Mask(Recipient));

    [Fact]
    public async Task Same_turn_twice_returns_the_first_actions()
    {
        var store = new MemoryTurnStore();
        var settings = new MutableSettings(Settings(echo: true));
        var service = new HandleTurn(store, settings, new EchoOnly());

        var first = await service.HandleAsync(Turn().ToTurn(), CancellationToken.None);
        settings.Current = Settings(echo: false); // a later change must not alter a replayed turn
        var second = await service.HandleAsync(Turn().ToTurn(), CancellationToken.None);

        Assert.Equal(TurnStatus.Processed, first.Status);
        Assert.Equal(TurnStatus.Duplicate, second.Status);
        Assert.Equal(first.Actions, second.Actions);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task Race_between_two_identical_turns_ends_as_one_duplicate()
    {
        var store = new MemoryTurnStore { LoseNextInsert = true };
        var response = await new HandleTurn(store, new MutableSettings(Settings(echo: true)), new EchoOnly()).HandleAsync(Turn().ToTurn(), CancellationToken.None);
        Assert.Equal(TurnStatus.Duplicate, response.Status);
    }

    private sealed class EchoOnly : IResponderSelector
    {
        public ITurnResponder Select(HazemSettings settings) => new EchoResponder();
    }

    private sealed class MutableSettings(HazemSettings current) : ISettingsReader
    {
        public HazemSettings Current { get; set; } = current;
        public Task<HazemSettings> GetAsync(CancellationToken ct) => Task.FromResult(Current);
    }

    private sealed class MemoryTurnStore : ITurnStore
    {
        public ConcurrentDictionary<string, StoredTurn> Rows { get; } = new();

        /// <summary>Simulates another request inserting the same turn between Find and TryAdd.</summary>
        public bool LoseNextInsert { get; set; }

        public Task<StoredTurn?> FindAsync(string turnId, CancellationToken ct) =>
            Task.FromResult(Rows.TryGetValue(turnId, out var t) ? t : null);

        public Task<bool> TryAddAsync(StoredTurn turn, Office.Application.Conversations.TurnEffects? effects, CancellationToken ct)
        {
            if (LoseNextInsert)
            {
                LoseNextInsert = false;
                Rows[turn.TurnId] = turn with { Responder = "other-request" };
                return Task.FromResult(false);
            }

            return Task.FromResult(Rows.TryAdd(turn.TurnId, turn));
        }
    }
}
