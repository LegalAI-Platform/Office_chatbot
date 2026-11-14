using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.114 (the owner, 2026-11-14, of a chat where the wife's age, corrected from under 18 right after the refusal, got
/// the closing text twice: «هو عدل السن… صلحها»). The age refusal now goes the way of the guardian's (v1.72): what the AI read
/// of the ages after it (the last said, v1.74; one side alone keeps the other) goes through the same B-11 rules. 18 or more on
/// both sides: the case goes on from where it stopped, said back first; under 18 still: the refusal stays, with its closing text.
/// </summary>
public sealed partial class BookingFlowTests
{
    private static UrfSaid AgesSaid(int? husband = null, int? wife = null) => new(UrfStep.Ages, UrfKind.Said, HusbandAge: husband, WifeAge: wife);

    [Fact]
    public async Task V114_an_age_corrected_to_18_or_more_after_the_refusal_goes_on_said_back_first()
    {
        await RefusedForTheAge(); // «الزوج 22 والزوجه 16»
        CaseSaidOn();
        CorrectedOn();
        _script["معلش نسيت 19 مش 16"] = new("service_by_name", "urf_marriage", "unknown", null, 0.8, CaseSaid: [AgesSaid(wife: 19)]);

        var d = await Say("معلش نسيت 19 مش 16");
        Assert.Equal([TextKeys.UrfAnswerCorrected, TextKeys.UrfAskWifePast], Keys(d));
        Assert.Contains("السن: الزوج 22 سنة والزوجة 19 سنة", d.Actions[0].Text);
        var now = _booking.UrfCases[^1];
        Assert.Equal((UrfCase.Collecting, (string?)UrfStep.WifePast, (int?)22, (int?)19), (now.State, now.Step, now.HusbandAge, now.WifeAge));
        Assert.Equal((UrfCase.RejectedAge, (int?)16), (_booking.UrfCases[0].State, _booking.UrfCases[0].WifeAge)); // the refused case stays
    }

    [Fact]
    public async Task V114_an_age_still_under_18_after_the_refusal_keeps_the_closing_text()
    {
        await RefusedForTheAge();
        CaseSaidOn();
        CorrectedOn();
        _script["لا قصدي 17"] = new("service_by_name", "urf_marriage", "unknown", null, 0.8, CaseSaid: [AgesSaid(wife: 17)]);

        Assert.Equal([TextKeys.UrfAgeClosed], Keys(await Say("لا قصدي 17")));
        Assert.Equal(UrfCase.RejectedAge, TheCase.State);
    }

    [Fact]
    public void V114_the_rules_open_a_case_refused_for_the_age_only_for_ages_they_accept()
    {
        var refused = new UrfCase(Guid.NewGuid(), Guid.NewGuid(), UrfCase.Marriage, UrfCase.RejectedAge, null, 30, 15) { PolicyVersion = UrfRules.PolicyVersion };

        var again = UrfRules.ApplyFacts(refused, null, null, null, wifeAge: 19);
        Assert.NotEqual(refused.Id, again.Id); // a new case; the refused one is never written again
        Assert.Equal((UrfCase.Collecting, (string?)UrfStep.WifePast, (int?)30, (int?)19, (string?)null),
            (again.State, again.Step, again.HusbandAge, again.WifeAge, again.PolicyVersion));
        Assert.Equal("السن: الزوج 30 سنة والزوجة 19 سنة", UrfRules.Corrected(refused, again));

        Assert.Equal(refused, UrfRules.ApplyFacts(refused, null, null, null, wifeAge: 17)); // still under 18
        Assert.Equal(refused, UrfRules.ApplyFacts(refused, null, null, null, husbandAge: 30, wifeAge: 15)); // the same ages
        Assert.Equal(refused, UrfRules.ApplyFacts(refused, null, null, null)); // nothing about the ages
        Assert.Equal(refused, UrfRules.ApplyFacts(refused, null, null, null, husbandAge: 16)); // the husband under 18 now
    }

    [Fact]
    public void V114_one_side_alone_corrects_the_pair_the_case_has_and_never_answers_the_ages_question()
    {
        var known = new UrfCase(Guid.NewGuid(), Guid.NewGuid(), UrfCase.Marriage, UrfCase.Collecting, UrfStep.WifePast, 26, 27);
        var corrected = UrfRules.ApplyFacts(known, null, null, null, wifeAge: 25);
        Assert.Equal(((int?)26, (int?)25), (corrected.HusbandAge, corrected.WifeAge));

        var asking = new UrfCase(Guid.NewGuid(), Guid.NewGuid(), UrfCase.Marriage, UrfCase.Collecting, UrfStep.Ages);
        Assert.Equal(asking, UrfRules.ApplyFacts(asking, null, null, null, wifeAge: 25)); // one age with none known: the question stays

        var held = known with { State = UrfCase.DocumentsPending, Step = null, AppointmentId = Guid.NewGuid() };
        Assert.True(UrfRules.ChangesBooked(held, null, null, null, wifeAge: 25)); // at a held time it is the office's (v1.62)
    }
}
