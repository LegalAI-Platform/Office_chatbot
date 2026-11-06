using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.82 (Ahmed Amin's chat, 2026-11-06): «اخوها مش هيجي» after «معاها اخوها» refused the case as «nobody of her
/// family», and «ينفع ابوها بدل اخوها؟» got the closing refusal that itself says the father may come. The guardian who will not
/// come («dropped») brings the guardian question back; only «nobody» refuses; the one he offers in a question is his answer, and
/// the answer that opens a refused case again is said back first.
/// </summary>
public sealed partial class BookingFlowTests
{
    private void CorrectedOn() =>
        _texts[TextKeys.UrfAnswerCorrected] = new ApprovedText(TextKeys.UrfAnswerCorrected, 1, "تمام، عدّلت {{changed}} ✅", 300, true);

    [Fact]
    public async Task The_named_guardian_who_will_not_come_brings_the_guardian_question_back_not_the_refusal()
    {
        UrfFactsOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Answer(UrfStep.Witnesses, "own")));

        _script["اخوها مش هيجي"] = new("service_by_name", "urf_marriage", "unknown", null, 0.85, UrfGuardian: "dropped");
        Assert.Equal([TextKeys.UrfAskGuardian], Keys(await Say("اخوها مش هيجي")));
        Assert.Equal((UrfCase.Collecting, UrfStep.Guardian, null, "own"), (TheCase.State, TheCase.Step, TheCase.Guardian, TheCase.Witnesses));

        var father = await Answer(UrfStep.Guardian, "father");
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(father));
        Assert.Contains("بطاقة الولي (الأب)", father.Actions[0].Text);
    }

    [Fact]
    public async Task Nobody_of_her_family_still_refuses_after_the_guardian_question_comes_back()
    {
        UrfFactsOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        _script["اخوها مش هيجي"] = new("service_by_name", "urf_marriage", "unknown", null, 0.85, UrfGuardian: "dropped");
        await Say("اخوها مش هيجي");
        Assert.Equal([TextKeys.UrfNoGuardian], Keys(await Answer(UrfStep.Guardian, "none")));
        Assert.Equal(UrfCase.RejectedGuardian, TheCase.State);
    }

    private void GuardianCanOn() =>
        _texts[TextKeys.UrfGuardianCan] = new ApprovedText(TextKeys.UrfGuardianCan, 1, "أيوه، {{guardian}} ينفع يبقى الولي. هو هيحضر معاكم وموافق؟", 300, true);

    private async Task ToTheRefusal()
    {
        UrfFactsOn();
        CorrectedOn();
        GuardianCanOn();
        await ToTheGuardian();
        _texts[TextKeys.UrfGuardianClosed] = new ApprovedText(TextKeys.UrfGuardianClosed, 1, "T:guardian closed", 300, true);
        Assert.Equal([TextKeys.UrfNoGuardian], Keys(await Answer(UrfStep.Guardian, "none")));
    }

    [Fact]
    public async Task A_question_about_a_relative_is_answered_and_asked_back_never_taken_as_he_comes()
    {
        UrfFactsOn();
        GuardianCanOn();
        await ToTheGuardian();
        _script["ينفع ابوها ؟"] = new("other", null, "unknown", null, 0.8, UrfGuardianAsked: "father");
        var d = await Say("ينفع ابوها ؟");
        Assert.Equal([TextKeys.UrfGuardianCan], Keys(d));
        Assert.Contains("أيوه، الأب ينفع يبقى الولي", d.Actions[0].Text);
        Assert.Null(TheCase.Guardian); // a question: nothing taken yet
        Assert.Equal([UrfAnswer.Id(UrfStep.Guardian, "father"), UrfAnswer.Id(UrfStep.Guardian, UrfAnswer.GuardianNo)],
            _lastChoices[Number]!.Select(c => c.Id).ToList());

        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(await Answer(UrfStep.Guardian, "father"))); // «أيوه»: he is the guardian
        Assert.Equal("father", TheCase.Guardian);
    }

    [Fact]
    public async Task No_under_the_asked_back_question_brings_the_guardian_question_back()
    {
        UrfFactsOn();
        GuardianCanOn();
        await ToTheGuardian();
        _script["لو عمها ينفع؟"] = new("other", null, "unknown", null, 0.8, UrfGuardianAsked: "uncle");
        Assert.Equal([TextKeys.UrfGuardianCan], Keys(await Say("لو عمها ينفع؟")));
        Assert.Equal([TextKeys.UrfAskGuardian], Keys(await Answer(UrfStep.Guardian, UrfAnswer.GuardianNo)));
        Assert.Equal((UrfCase.Collecting, UrfStep.Guardian, (string?)null), (TheCase.State, TheCase.Step, TheCase.Guardian));
    }

    [Fact]
    public async Task After_the_refusal_yes_opens_the_case_again_said_back_first()
    {
        await ToTheRefusal();
        _script["ينفع ابوها بدل اخوها ؟"] = new("other", null, "unknown", null, 0.8, UrfGuardianAsked: "father");
        Assert.Equal([TextKeys.UrfGuardianCan], Keys(await Say("ينفع ابوها بدل اخوها ؟")));

        var d = await Answer(UrfStep.Guardian, "father");
        Assert.Equal([TextKeys.UrfAnswerCorrected, TextKeys.UrfAskWitnesses], Keys(d));
        Assert.Contains("الولي يبقى الأب", d.Actions[0].Text);
        Assert.Equal((UrfCase.Collecting, "father"), (_booking.UrfCases[^1].State, _booking.UrfCases[^1].Guardian));
        Assert.Equal(UrfCase.RejectedGuardian, _booking.UrfCases[0].State); // the refused case stays as it was
    }

    [Fact]
    public async Task After_the_refusal_no_keeps_the_refusal()
    {
        await ToTheRefusal();
        _script["ينفع عمها؟"] = new("other", null, "unknown", null, 0.8, UrfGuardianAsked: "uncle");
        await Say("ينفع عمها؟");
        Assert.Equal([TextKeys.UrfGuardianClosed], Keys(await Answer(UrfStep.Guardian, UrfAnswer.GuardianNo)));
        Assert.Single(_booking.UrfCases);
    }

    [Fact]
    public async Task Saying_he_comes_after_the_refusal_is_said_back_and_goes_on_at_once()
    {
        await ToTheRefusal();
        _script["ابوها موجود"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9, UrfGuardian: "father");
        var d = await Say("ابوها موجود");
        Assert.Equal([TextKeys.UrfAnswerCorrected, TextKeys.UrfAskWitnesses], Keys(d));
        Assert.Contains("الولي يبقى الأب", d.Actions[0].Text);
    }

    [Fact]
    public async Task Another_relative_he_asks_about_keeps_the_refusal()
    {
        await ToTheRefusal();
        _script["ينفع خالها؟"] = new("other", null, "unknown", null, 0.8); // classify-v28: not one of the three, null
        Assert.Equal([TextKeys.UrfGuardianClosed], Keys(await Say("ينفع خالها؟")));
    }

    [Fact]
    public void A_dropped_guardian_at_a_held_time_goes_to_the_office()
    {
        var held = new UrfCase(Guid.NewGuid(), Guid.NewGuid(), UrfCase.Marriage, UrfCase.DocumentsPending, null, 30, 25, "first", "brother", "own")
        {
            AppointmentId = Guid.NewGuid(),
        };
        Assert.True(UrfRules.ChangesBooked(held, null, "dropped", null));
        Assert.Equal(held, UrfRules.ApplyFacts(held, null, "dropped", null)); // nothing changes by itself
    }

    [Fact]
    public void Dropped_never_refuses_and_never_opens_a_refused_case()
    {
        var refused = new UrfCase(Guid.NewGuid(), Guid.NewGuid(), UrfCase.Marriage, UrfCase.RejectedGuardian, null, 30, 25, "first", "none", "own");
        Assert.Equal(refused, UrfRules.ApplyFacts(refused, null, "dropped", null));
        var waiting = new UrfCase(Guid.NewGuid(), Guid.NewGuid(), UrfCase.Marriage, UrfCase.DocumentsPending, null, 30, 25, "first", "uncle", "office");
        var asked = UrfRules.ApplyFacts(waiting, null, "dropped", null);
        Assert.Equal((UrfCase.Collecting, UrfStep.Guardian, (string?)null), (asked.State, asked.Step, asked.Guardian));
        Assert.Null(UrfRules.Corrected(waiting, asked)); // nothing to say back: the question asks
    }
}
