using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Urf;
using Office.Application.Writing;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.68 (the owner): «اتطلقت أو اختلعت» and «أرملة»; the guardian is asked only for a first marriage; «اول مره ليها»
/// is «أول جوازة» by the code; the name question after a hold always goes as approved.
/// </summary>
public sealed partial class BookingFlowTests
{
    private async Task ToTheWifePast()
    {
        UrfOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        _script["30 و 25"] = new("other", null, "unknown", null, 0.9);
        Assert.Equal([TextKeys.UrfAskWifePast], Keys(await Say("30 و 25")));
    }

    [Fact]
    public async Task A_widow_is_never_asked_the_guardian_and_brings_the_death_certificate()
    {
        await ToTheWifePast();
        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(await Answer(UrfStep.WifePast, "widow")));
        var times = await Answer(UrfStep.Witnesses, "own");
        var text = times.Actions.Single().Text!;
        Assert.Contains("شهادة وفاة الزوج السابق", text);
        Assert.DoesNotContain("بطاقة الولي", text);
        Assert.Equal("الزوج والزوجة والشاهدين", TheCase.Attendees());
    }

    [Fact]
    public async Task A_divorce_or_a_khul_is_one_answer_with_one_paper_and_no_guardian()
    {
        await ToTheWifePast();
        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(await Answer(UrfStep.WifePast, "divorced")));
        var text = (await Answer(UrfStep.Witnesses, "office")).Actions.Single().Text!;
        Assert.Contains("مستند الطلاق أو حكم الخلع", text);
        Assert.DoesNotContain("بطاقة الولي", text);
        Assert.Equal("الزوج والزوجة", TheCase.Attendees());
    }

    [Fact]
    public async Task A_first_marriage_still_asks_the_guardian_and_brings_his_card()
    {
        await ToTheWifePast();
        Assert.Equal([TextKeys.UrfAskGuardian], Keys(await Answer(UrfStep.WifePast, "first")));
        await Answer(UrfStep.Guardian, "brother");
        var text = (await Answer(UrfStep.Witnesses, "own")).Actions.Single().Text!;
        Assert.Contains("بطاقة الولي (الأخ)", text);
        Assert.Equal("الزوج والزوجة والولي والشاهدين", TheCase.Attendees());
    }

    [Fact]
    public async Task The_wife_past_question_offers_first_divorce_or_khul_and_widow()
    {
        await ToTheWifePast();
        var ids = _lastChoices[Number]!.Select(c => c.Id).ToList();
        Assert.Equal([UrfAnswer.Id(UrfStep.WifePast, "first"), UrfAnswer.Id(UrfStep.WifePast, "divorced"), UrfAnswer.Id(UrfStep.WifePast, "widow")], ids);
    }
}

public sealed class UrfWidowRuleTests
{
    private static readonly InteractiveChoice[] Past =
    [
        new("urf:wife_past:first", "أول جوازة"), new("urf:wife_past:divorced", "اتطلقت أو اختلعت"), new("urf:wife_past:widow", "أرملة"),
    ];

    [Theory]
    [InlineData("اول مره ليها", "urf:wife_past:first")] // the short real word «اول», with one row only
    [InlineData("أول مرة", "urf:wife_past:first")]
    [InlineData("ارمله", "urf:wife_past:widow")]
    [InlineData("كانت متجوزة قبل كده؟", null)]           // a question
    [InlineData("مش اول مره", null)]                     // a refusal
    public void Short_words_shared_with_one_row_pick_it(string words, string? id) => Assert.Equal(id, TypedChoice.ByWords(Past, words)?.Id);

    [Fact]
    public void The_name_question_after_a_hold_is_never_in_the_writer_s_words() =>
        Assert.False(Conversational.MayWrite(TextKeys.AskNameBooking));

    [Fact]
    public void The_guardian_question_is_for_a_first_marriage_only()
    {
        var c = UrfCase.Start(Guid.NewGuid(), UrfCase.Marriage) with { HusbandAge = 30, WifeAge = 25 };
        Assert.Equal(UrfStep.Guardian, UrfRules.NextStep(c with { WifePast = "first" }));
        Assert.Equal(UrfStep.Witnesses, UrfRules.NextStep(c with { WifePast = "divorced" }));
        Assert.Equal(UrfStep.Witnesses, UrfRules.NextStep(c with { WifePast = "khul" }));
        Assert.Equal(UrfStep.Witnesses, UrfRules.NextStep(c with { WifePast = "widow" }));
    }
}
