using Office.Application.Costs;
using Office.Application.Texts;
using Office.Application.Understanding;
using Office.Application.Writing;

namespace Office.UnitTests;

/// <summary>
/// v1.58 (the owner: «المحادثة صرفت قد إيه من الكريدت؟ خد كام توكن»): the price of a call, and every AI call of a turn (the
/// classifier, the writer, its retry, the reviewer) in the turn's effects, saved to hazem.ai_usage.
/// </summary>
public sealed class AiPricesTests
{
    [Fact]
    public void A_call_costs_its_tokens_at_the_price_of_each_kind()
    {
        // 500 uncached at $4, 14000 read from the cache at $0.20, 190 out at $20, per million
        Assert.Equal(0.0086m, AiPrices.Cost("claude-opus-5-5", new TokenUsage(500, 14000, 0, 190)));
        // the cache write is a quarter more than the input
        Assert.Equal(0.005m, AiPrices.Cost("claude-opus-5-5", new TokenUsage(0, 0, 1000, 0))); // 1000 written to the cache at $5
        Assert.Equal(0.000495m, AiPrices.Cost("claude-haiku-5-5", new TokenUsage(4400, 0, 0, 110)));
    }

    [Fact]
    public void The_longest_model_id_wins_and_a_model_with_no_price_costs_nothing_known()
    {
        Assert.Equal(0.000004m, AiPrices.Cost("claude-opus-5-5", new TokenUsage(1, 0, 0, 0)));       // $4, not Opus 5's $5
        Assert.Equal(0.000005m, AiPrices.Cost("claude-opus-5", new TokenUsage(1, 0, 0, 0)));
        Assert.Null(AiPrices.Cost("gpt-5-mini", new TokenUsage(1000, 0, 0, 100)));
    }
}

public sealed partial class BookingFlowTests
{
    [Fact]
    public async Task A_written_reply_leaves_the_usage_of_the_classifier_the_writer_and_the_reviewer()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Tokens = new TokenUsage(4400, 0, 0, 110);
        _reviewer.Tokens = new TokenUsage(1500, 0, 0, 20);
        _writer.Reply = "متقلقش يا فندم 🙏 كلامك مع المكتب سري.";
        var d = await Say("انا خايف مراتي تعرف");
        var usage = d.Effects!.Usage!;
        Assert.Equal([AiUsageKind.Classify, AiUsageKind.Write, AiUsageKind.Review], usage.Select(u => u.Kind).ToList());
        Assert.Equal(new TokenUsage(4400, 0, 0, 110), usage[1].Tokens);
        Assert.Equal(new TokenUsage(1500, 0, 0, 20), usage[2].Tokens);
        Assert.Equal("fake", usage[0].Model);
    }

    [Fact]
    public async Task A_retry_is_counted_too_the_second_write_and_the_second_review()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Tokens = new TokenUsage(4400, 0, 0, 110);
        _reviewer.Tokens = new TokenUsage(1500, 0, 0, 20);
        _writer.Then.Enqueue("متقلقش يا فندم، مراتك مش هتعرف أبدًا 🙏");
        _writer.Then.Enqueue("متقلقش يا فندم 🙏 كلامك مع المكتب سري.");
        _reviewer.Sequence.Enqueue(ReplyReview.Promise);
        _reviewer.Sequence.Enqueue(null);
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal([AiUsageKind.Classify, AiUsageKind.Write, AiUsageKind.Review, AiUsageKind.Write, AiUsageKind.Review],
            d.Effects!.Usage!.Select(u => u.Kind).ToList());
    }

    [Fact]
    public async Task With_the_switch_off_there_is_no_review_to_count_and_a_turn_with_no_ai_call_leaves_nothing()
    {
        FreeAnswerOn();
        _settings[UnderstandingV2.SwitchKey] = "false";
        await ToTheOffer();
        _writer.Tokens = new TokenUsage(4400, 0, 0, 110);
        _reviewer.Tokens = new TokenUsage(1500, 0, 0, 20);
        _writer.Reply = "أهلًا بحضرتك 👋 أقدر أساعدك في إيه النهارده؟";
        var d = await Say("السلام عليكم");
        Assert.DoesNotContain(d.Effects!.Usage!, u => u.Kind == AiUsageKind.Review);
    }
}
