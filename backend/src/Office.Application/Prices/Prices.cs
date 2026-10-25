using System.Globalization;
using Office.Application.Booking;
using Office.Application.Responders;

namespace Office.Application.Prices;

// TASK 06 (DECISIONS v1.17, B-23): the price list is settings (price.*), edited from the panel by the lawyer and the manager.
// Every amount the system asks for and every price a text quotes comes from here, so they never disagree. A new price counts
// for new requests only: what was booked or ordered keeps the amount stored with it.

/// <summary>One line of B-23: its setting, the slot texts quote it with, its Arabic name and its 1.4 value.</summary>
public sealed record PriceLine(string Key, string Slot, string Name, int Default);

public sealed class PriceList
{
    public const string PhoneVideo = "price.phone_video";
    public const string Office = "price.office";
    public const string Urgent = "price.urgent";
    public const string ReviewMin = "price.review_min";
    public const string ReviewMax = "price.review_max";
    public const string DraftingFrom = "price.drafting_from";
    public const string FormalNotice = "price.formal_notice";
    public const string WrittenOpinion = "price.written_opinion";
    public const string UrfOfficeWitnesses = "price.urf_office_witnesses";
    public const string UrfOwnWitnesses = "price.urf_own_witnesses";

    /// <summary>The highest price the panel accepts (the SQL's hazem.price reads at most 7 digits).</summary>
    public const int Max = 1_000_000;

    /// <summary>B-23 as the lawyer set it in 1.4: the starting values, and the fallback for a missing or broken setting.</summary>
    public static IReadOnlyList<PriceLine> Lines { get; } =
    [
        new(PhoneVideo, "price_phone", "استشارة بالتليفون أو الفيديو (ربع ساعة)", 500),
        new(Office, "price_office", "مقابلة في المكتب (نص ساعة)", 1000),
        new(Urgent, "price_urgent", "استشارة عاجلة بالتليفون (ربع ساعة)", 1000),
        new(ReviewMin, "price_review_min", "مراجعة عقد أو مستندات: من", 1500),
        new(ReviewMax, "price_review_max", "مراجعة عقد أو مستندات: لحد", 3000),
        new(DraftingFrom, "price_drafting_from", "صياغة عقد: من", 3000),
        new(FormalNotice, "price_notice", "إنذار رسمي", 2000),
        new(WrittenOpinion, "price_opinion", "رأي قانوني مكتوب (فوق الاستشارة)", 2000),
        new(UrfOfficeWitnesses, "price_urf_office", "جواز أو طلاق عرفي، شهود المكتب", 5000),
        new(UrfOwnWitnesses, "price_urf_own", "جواز أو طلاق عرفي، شهود العميل", 4000),
    ];

    private readonly IReadOnlyDictionary<string, int> _values;

    private PriceList(IReadOnlyDictionary<string, int> values) => _values = values;

    public static PriceList Default { get; } = new(Lines.ToDictionary(l => l.Key, l => l.Default));

    /// <summary>The panel's prices; a missing or broken one is B-23's (as hazem.price in SQL).</summary>
    public static PriceList From(HazemSettings s) =>
        new(Lines.ToDictionary(l => l.Key, l => Valid(s.Get(l.Key)) is { } v ? v : l.Default));

    public int this[string key] => _values[key];

    /// <summary>The amount a booking of this service asks for now.</summary>
    public int For(BookingService service) => service.Code switch
    {
        "consultation_phone_video" => this[PhoneVideo],
        "consultation_office" => this[Office],
        "consultation_urgent" => this[Urgent],
        "urf_marriage_office" or "urf_divorce_office" => this[UrfOfficeWitnesses],
        "urf_marriage_own" or "urf_divorce_own" => this[UrfOwnWitnesses],
        _ => service.Amount,
    };

    /// <summary>The {{price_*}} slots of the texts, as Hazem writes money («1,000»).</summary>
    public IReadOnlyDictionary<string, string> Slots() => Lines.ToDictionary(l => l.Slot, l => BookingWords.Money(this[l.Key]), StringComparer.Ordinal);

    /// <summary>Fills the price slots of a text; every other slot is left for its own step.</summary>
    public string Fill(string text)
    {
        if (!text.Contains("{{price_", StringComparison.Ordinal))
        {
            return text;
        }

        foreach (var (slot, value) in Slots())
        {
            text = text.Replace("{{" + slot + "}}", value, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>A whole number from 1 to <see cref="Max"/> written plainly (no leading zero, as hazem.price in SQL), or null.</summary>
    public static int? Valid(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v is >= 1 and <= Max
        && value == v.ToString(CultureInfo.InvariantCulture) ? v : null;

    /// <summary>What is wrong with a whole list before it is saved: the review's range upside down, or a price out of bounds.</summary>
    public static string? Problem(IReadOnlyDictionary<string, int> values)
    {
        if (Lines.Any(l => !values.TryGetValue(l.Key, out var v) || v is < 1 or > Max))
        {
            return "invalid_price";
        }

        return values[ReviewMin] > values[ReviewMax] ? "review_range" : null;
    }
}
