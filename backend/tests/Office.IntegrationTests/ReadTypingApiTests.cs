namespace Office.IntegrationTests;

/// <summary>
/// v1.69 (the owner: «ليه مش ريل تايم؟»): n8n marks a message read as it arrives, with «بيكتب…» only when Hazem will answer
/// (app.wa_hazem_answers): Hazem is the responder, the number is one he answers, and the chat is not with the office.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class ReadTypingApiTests(TestDatabase db) : IAsyncLifetime
{
    private const string Bot = "201000000171", Office = "201000000172", Stranger = "201000000173";

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync($"""
            DELETE FROM hazem.conversations c USING hazem.persons p WHERE p.id = c.person_id AND p.wa_id IN ('{Bot}', '{Office}');
            DELETE FROM hazem.persons WHERE wa_id IN ('{Bot}', '{Office}') AND NOT EXISTS (SELECT 1 FROM hazem.intakes i WHERE i.person_id = hazem.persons.id);
            """);
        await db.SetAsync("responder", "hazem");
        await db.SetAsync("hazem.open_to_all", "false");
        await db.SetAsync("hazem.recipients", $"{Bot},{Office}");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<bool> Answers(string wa) => db.ScalarAsync<bool>($"SELECT app.wa_hazem_answers('{wa}')");

    [Fact]
    public async Task V169_typing_only_when_hazem_answers_this_number_and_the_chat_is_not_with_the_office()
    {
        var (bot, office) = (Guid.NewGuid(), Guid.NewGuid());
        await db.ExecAsync($"""
            INSERT INTO hazem.persons (id, wa_id) VALUES ('{bot}', '{Bot}'), ('{office}', '{Office}');
            INSERT INTO hazem.conversations (id, person_id, state) VALUES (gen_random_uuid(), '{bot}', 'bot_active'), (gen_random_uuid(), '{office}', 'human_handoff');
            """);
        Assert.True(await Answers(Bot));
        Assert.False(await Answers(Office));   // with the office: the ticks only, never «بيكتب…»
        Assert.False(await Answers(Stranger)); // not a number Hazem answers yet
        await db.SetAsync("hazem.open_to_all", "true");
        Assert.True(await Answers(Stranger));  // a new client, once Hazem answers everyone
        await db.SetAsync("hazem.open_to_all", "false");
        Assert.True(await db.ScalarAsync<bool>("SELECT has_function_privilege('office_app', 'app.wa_hazem_answers(text)', 'execute')"));
    }
}
