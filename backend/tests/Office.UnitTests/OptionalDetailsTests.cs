using Office.Application.Texts;

namespace Office.UnitTests;

/// <summary>
/// v1.51: an extra office detail not written in the panel (its phone, the map link) drops its own sentence, never the whole
/// text (Ahmed Amin's confirmation was never sent for a missing office phone). Any other slot still stops the text.
/// </summary>
public sealed class OptionalDetailsTests
{
    private const string Confirmed = "تم تأكيد المقابلة {{day}} الساعة {{time}} ✅، المكتب في {{address}}، وده اللوكيشن {{map}}.\nلو احتجت حاجة كلمنا على {{office_phone}}.";

    private static string? Render(string text, Dictionary<string, string> fill) =>
        TextRenderer.Render(new ApprovedText("k", 1, text, 600, true), fill).Text;

    private static Dictionary<string, string> Fill(params (string Key, string Value)[] extra)
    {
        var fill = new Dictionary<string, string> { ["day"] = "النهارده", ["time"] = "10:15 ص", ["address"] = "1 شارع التحرير" };
        foreach (var (k, v) in extra)
        {
            fill[k] = v;
        }

        return fill;
    }

    [Fact]
    public void The_confirmation_goes_without_the_phone_line_when_the_office_phone_is_missing() =>
        Assert.Equal("تم تأكيد المقابلة النهارده الساعة 10:15 ص ✅، المكتب في 1 شارع التحرير، وده اللوكيشن https://m.test.",
            Render(Confirmed, Fill(("map", "https://m.test"))));

    [Fact]
    public void Without_the_map_only_its_clause_goes() =>
        Assert.Equal("تم تأكيد المقابلة النهارده الساعة 10:15 ص ✅، المكتب في 1 شارع التحرير.\nلو احتجت حاجة كلمنا على 0100.",
            Render(Confirmed, Fill(("office_phone", "0100"))));

    [Fact]
    public void Without_both_the_text_still_goes_with_what_it_has() =>
        Assert.Equal("تم تأكيد المقابلة النهارده الساعة 10:15 ص ✅، المكتب في 1 شارع التحرير.", Render(Confirmed, Fill()));

    [Theory]
    [InlineData("المكتب في {{address}}، واللوكيشن: {{map}}", "المكتب في 1 شارع التحرير")]
    [InlineData("📍 عنوان المكتب: {{address}}.\nالخريطة: {{map}}", "📍 عنوان المكتب: 1 شارع التحرير.")]
    public void The_other_texts_with_the_map_lose_only_its_line(string text, string expected) =>
        Assert.Equal(expected, Render(text, Fill()));

    [Fact]
    public void A_missing_required_detail_still_stops_the_text()
    {
        var fill = Fill(("map", "https://m.test"), ("office_phone", "0100"));
        fill.Remove("address");
        Assert.Null(Render(Confirmed, fill));
    }
}
