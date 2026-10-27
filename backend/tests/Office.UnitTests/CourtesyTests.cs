using Office.Application.Understanding;

namespace Office.UnitTests;

/// <summary>DECISIONS v1.21 (B-01.6): the greetings read by code, every spelling the office gets.</summary>
public sealed class CourtesyTests
{
    [Theory]
    [InlineData("السلام عليكم", CourtesyKind.Salam)]
    [InlineData("السَّلامُ عَلَيْكُمْ", CourtesyKind.Salam)]
    [InlineData("سلام عليكم", CourtesyKind.Salam)]
    [InlineData("سلامو عليكو", CourtesyKind.Salam)]
    [InlineData("السلام عليكم ورحمة الله وبركاته", CourtesyKind.Salam)]
    [InlineData("سلام", CourtesyKind.Salam)]
    [InlineData("Salam alaikum", CourtesyKind.Salam)]
    [InlineData("assalamu alaikum", CourtesyKind.Salam)]
    [InlineData("صباح الخير", CourtesyKind.Morning)]
    [InlineData("صباح الفل يا فندم", CourtesyKind.Morning)]
    [InlineData("good morning", CourtesyKind.Morning)]
    [InlineData("مساء الخير", CourtesyKind.Evening)]
    [InlineData("مسا الخير", CourtesyKind.Evening)]
    [InlineData("إزيك", CourtesyKind.HowAreYou)]
    [InlineData("عامل ايه", CourtesyKind.HowAreYou)]
    [InlineData("اخبارك ايه", CourtesyKind.HowAreYou)]
    [InlineData("رمضان كريم", CourtesyKind.Ramadan)]
    [InlineData("جمعة مباركة", CourtesyKind.Jumua)]
    [InlineData("كل سنة وانت طيب", CourtesyKind.Occasion)]
    [InlineData("عيد مبارك", CourtesyKind.Occasion)]
    [InlineData("تصبح على خير", CourtesyKind.GoodNight)]
    [InlineData("مع السلامة", CourtesyKind.Goodbye)]
    [InlineData("باي", CourtesyKind.Goodbye)]
    [InlineData("في رعاية الله", CourtesyKind.Goodbye)]
    public void Every_greeting_is_read(string text, CourtesyKind kind) => Assert.Equal(kind, Courtesy.Read(text).Kind);

    [Theory]
    [InlineData("عايز أحجز استشارة")]
    [InlineData("الإسلام بيقول إيه في الميراث")]
    [InlineData("مستعجل جدا")]
    [InlineData("شكرا")] // the thanks stay the classifier's
    [InlineData("")]
    [InlineData(null)]
    public void Other_words_are_no_greeting(string? text) => Assert.Equal(CourtesyKind.None, Courtesy.Read(text).Kind);

    [Theory]
    [InlineData("السلام عليكم", true)]
    [InlineData("السلام عليكم يا فندم 🙏", true)]
    [InlineData("سلاااام عليكم", true)]
    [InlineData("السلام عليكم، عايز أحجز استشارة", false)]
    [InlineData("صباح الخير عندي سؤال", false)]
    public void Only_says_whether_the_message_is_the_greeting_alone(string text, bool only)
    {
        var m = Courtesy.Read(text);
        Assert.NotEqual(CourtesyKind.None, m.Kind);
        Assert.Equal(only, m.Only);
    }

    [Fact]
    public void The_first_greeting_of_several_is_answered() =>
        Assert.Equal(CourtesyKind.Salam, Courtesy.Read("صباح الخير، السلام عليكم، ازيك").Kind);

    [Theory]
    [InlineData("مع السلامة", true)]
    [InlineData("تصبح على خير يا حازم", true)]
    [InlineData("مع السلامة، وابعتلي الميعاد", false)]
    [InlineData("السلام عليكم", false)]
    public void Only_a_goodbye_alone_ends_the_chat(string text, bool ends) => Assert.Equal(ends, Courtesy.Read(text).EndsTheChat);
}
