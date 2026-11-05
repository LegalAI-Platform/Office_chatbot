using Office.Application.Booking;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.65 (the owner's tests of 2026-11-05): under the list «الأب / الأخ / العم / مفيش حد منهم» the client wrote «ولا حد
/// فيهم» and then «مفيش حد منهم» (the row's own words) and was handed to the office; under «واتساب / FaceTime / مكالمة عادية»
/// «موبايلي عادي» was answered «تمام» while nothing was registered. Words that ARE a choice (its title, or its number) are that
/// choice by the code; a time already past brings the next free times.
/// </summary>
public sealed class TypedChoiceWordsTests
{
    private static readonly InteractiveChoice[] Guardian =
    [
        new("urf:guardian:father", "الأب"), new("urf:guardian:brother", "الأخ"), new("urf:guardian:uncle", "العم"),
        new("urf:guardian:none", "مفيش حد منهم"),
    ];

    private static readonly InteractiveChoice[] Call =
    [
        new("call:whatsapp", "🟢 واتساب"), new("call:facetime", "🍏 FaceTime (آيفون)"), new("call:phone", "📞 مكالمة عادية"),
    ];

    private static string? Id(IReadOnlyList<InteractiveChoice> waiting, string words) => TypedChoice.ByWords(waiting, words)?.Id;

    [Theory]
    [InlineData("مفيش حد منهم", "urf:guardian:none")] // the row's own words
    [InlineData("مفيش حد منهم.", "urf:guardian:none")]
    [InlineData("الأب", "urf:guardian:father")]
    [InlineData("الاب موجود", "urf:guardian:father")]   // spelling and a few words more
    [InlineData("اخوها", null)]                         // not the row's words: the AI reads it
    [InlineData("ولا حد فيهم", null)]                   // not the row's words: the AI reads it (classify-v19)
    [InlineData("مش الأب", null)]                       // a refusal is never a pick
    [InlineData("2", "urf:guardian:brother")]
    [InlineData("رقم 3", "urf:guardian:uncle")]
    [InlineData("التاني", "urf:guardian:brother")]
    [InlineData("الرابع", "urf:guardian:none")]
    [InlineData("9", null)]                             // outside the list
    [InlineData("عايز أعرف الأسعار", null)]
    public void Words_that_are_a_guardian_row_are_that_row(string words, string? id) => Assert.Equal(id, Id(Guardian, words));

    [Theory]
    [InlineData("واتساب", "call:whatsapp")]
    [InlineData("فيس تايم", null)]
    [InlineData("FaceTime", "call:facetime")]
    [InlineData("مكالمة عادية", "call:phone")]
    [InlineData("📞 مكالمة عادية", "call:phone")]
    [InlineData("موبايلي عادي", null)] // a meaning, not the row's words: the AI picks it (classify-v19)
    public void Words_that_are_a_way_of_calling_are_that_way(string words, string? id) => Assert.Equal(id, Id(Call, words));

    [Fact]
    public void A_bare_number_is_an_hour_not_a_row_when_the_rows_are_times()
    {
        var at = new DateTimeOffset(2026, 11, 5, 19, 0, 0, TimeSpan.FromHours(2));
        InteractiveChoice[] times =
        [
            new(BookingChoice.Pick(BookingService.PhoneVideo, at), "النهارده 7:00 م"),
            new(BookingChoice.Pick(BookingService.PhoneVideo, at.AddMinutes(15)), "النهارده 7:15 م"),
        ];
        Assert.Null(TypedChoice.ByWords(times, "2"));
        Assert.Equal(times[0].Id, TypedChoice.ByWords(times, "النهارده 7:00 م")!.Id); // a row said in full is still that row
    }

    [Fact]
    public void A_time_already_past_asks_for_the_times_from_now_not_nothing()
    {
        var now = new DateTimeOffset(2026, 11, 5, 16, 41, 0, TimeSpan.FromHours(2));
        var times = new InteractiveChoice[]
        {
            new(BookingChoice.Pick(BookingService.PhoneVideo, now.AddMinutes(124)), "النهارده 6:45 م"),
            new(BookingChoice.More(BookingService.PhoneVideo, now.AddHours(3)), "مواعيد تانية"),
        };
        var ai = new RawClassification("other", null, "unknown", null, 0.8, AskedTime: "2026-11-05T16:00", TimeExact: true);
        var asked = TypedChoice.Times(times, ai, now);
        Assert.NotNull(asked);
        Assert.Equal((now, false), (asked!.At, asked.Exact)); // «الساعة 4» at 4:41: not exact, the free times from now
    }
}

public sealed partial class BookingFlowTests
{
    /// <summary>A new client up to the guardian list, with the rows' words as the owner's panel has them (the list is sent with them).</summary>
    private async Task ToTheGuardianListWithWords()
    {
        UrfOn();
        foreach (var (k, v) in new Dictionary<string, string>
                 {
                     [TextKeys.BtnUrfFather] = "الأب", [TextKeys.BtnUrfBrother] = "الأخ", [TextKeys.BtnUrfUncle] = "العم",
                     [TextKeys.BtnUrfNoGuardian] = "مفيش حد منهم",
                 })
        {
            _texts[k] = new ApprovedText(k, 1, v, 20, true);
        }

        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        _script["30 و 25"] = new("other", null, "unknown", null, 0.9);
        await Say("30 و 25");
        Assert.Equal([TextKeys.UrfAskGuardian], Keys(await Answer(UrfStep.WifePast, "first")));
    }

    [Fact]
    public async Task The_row_s_own_words_typed_under_the_guardian_list_are_that_row_no_office_hand_over()
    {
        await ToTheGuardianListWithWords();
        var d = await Say("مفيش حد منهم");
        Assert.Equal([TextKeys.UrfNoGuardian], Keys(d)); // the office's policy text, never «علشان نكمّل…» or the office
        Assert.Equal(UrfCase.RejectedGuardian, TheCase.State);
        Assert.Null(d.Effects!.Classification); // read by the code: no AI call
    }

    [Fact]
    public async Task A_guardian_said_by_his_name_is_taken_the_same_way()
    {
        await ToTheGuardianListWithWords();
        var d = await Say("الأخ");
        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(d));
        Assert.Equal("brother", TheCase.Guardian);
    }
}
