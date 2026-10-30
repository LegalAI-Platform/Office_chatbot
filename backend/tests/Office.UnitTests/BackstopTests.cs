using Office.Application.Understanding;

namespace Office.UnitTests;

/// <summary>0032: when the AI could not read a message, an insult or a plain thanks is still read by code.</summary>
public sealed class BackstopTests
{
    private static readonly ClassifierOutcome Failed = new(null, ClassifierOutcome.Error, "claude-opus-5-5", "classify-v11", 40);

    private static Classification Down(string reason = ClassifierOutcome.Error) => Classification.Fallback(reason, Failed);

    [Theory]
    [InlineData("محتاج انيكك")]
    [InlineData("كسمك")]
    [InlineData("كس امك يا حازم")]
    [InlineData("يا متناك")]
    [InlineData("انت شرموط")]
    [InlineData("يا ابن الكلب")]
    [InlineData("fuck you")]
    public void An_insult_is_abusive_when_the_ai_is_down(string text)
    {
        var c = Backstop.Apply(Down(), text);
        Assert.Equal(Intent.Abusive, c.Intent);
        Assert.False(c.IsFallback);
        Assert.Equal("backstop:error", c.LoggedReason);
    }

    [Theory]
    [InlineData("شكرا ليك")]
    [InlineData("شكراً جداً يا أستاذ")]
    [InlineData("متشكر")]
    [InlineData("تسلم إيدك")]
    [InlineData("ميرسي 🙏")]
    [InlineData("thanks")]
    [InlineData("جزاك الله خيرا")]
    public void A_plain_thanks_is_a_thanks_when_the_ai_is_down(string text) =>
        Assert.Equal(Intent.ThanksOrClosing, Backstop.Apply(Down(), text).Intent);

    [Theory]
    [InlineData("شكرا، بس عايز أعرف الميعاد امتى")] // a thanks with a question: still unclear without the AI
    [InlineData("عندي مشكلة في عقد الإيجار")]
    [InlineData("التكنيك بتاع المحامي")] // نيك inside a word is no insult
    [InlineData("أنا زبون عندكم")] // زب inside a word
    [InlineData("شكري محمد")] // a name
    public void Anything_else_stays_unclear(string text)
    {
        var c = Backstop.Apply(Down(), text);
        Assert.Equal(Intent.Other, c.Intent);
        Assert.Equal(ClassifierOutcome.Error, c.FallbackReason);
    }

    [Fact]
    public void Every_ai_failure_counts_but_a_low_confidence_is_the_ai_s_own_answer()
    {
        foreach (var reason in new[] { ClassifierOutcome.NoKey, ClassifierOutcome.Timeout, ClassifierOutcome.InvalidOutput })
        {
            Assert.Equal(Intent.Abusive, Backstop.Apply(Down(reason), "كسمك").Intent);
        }

        Assert.Equal(Intent.Other, Backstop.Apply(Down(Classification.LowConfidence), "كسمك").Intent);
        var answered = Classification.Fallback(ClassifierOutcome.Error, Failed) with { Intent = Intent.LegalQuestion, FallbackReason = null };
        Assert.Same(answered, Backstop.Apply(answered, "شكرا"));
    }
}
