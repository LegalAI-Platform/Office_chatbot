using System.Text;
using System.Text.RegularExpressions;

namespace Office.Domain.Common;

/// <summary>A WhatsApp id: the client's identity for conversations and sends (B-03).</summary>
public sealed partial record WaId
{
    private WaId(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? value, out WaId waId)
    {
        waId = new WaId(value ?? "");
        return value is not null && Pattern().IsMatch(value);
    }

    public static WaId From(string value) =>
        TryCreate(value, out var w) ? w : throw new ArgumentException("wa_id must be 8-15 digits", nameof(value));

    public override string ToString() => Value;

    [GeneratedRegex("^[0-9]{8,15}$")]
    private static partial Regex Pattern();
}

/// <summary>
/// A client's name (B-02): <see cref="Raw"/> as typed, <see cref="Normalized"/> without extra
/// spaces, tashkeel or tatweel. "عبد"/"أبو" + the next word count as one name, so
/// "محمد عبد الرحمن أحمد علي" is four names.
/// </summary>
public sealed partial record PersonName
{
    public const int FullNameParts = 4;
    public const int MaxLength = 120;

    private static readonly HashSet<string> JoinsNextWord = new(StringComparer.Ordinal) { "عبد", "ابو", "أبو", "إبو" };

    private PersonName(string raw, string normalized, int parts)
    {
        Raw = raw;
        Normalized = normalized;
        Parts = parts;
    }

    public string Raw { get; }

    public string Normalized { get; }

    public int Parts { get; }

    public bool IsFull => Parts >= FullNameParts;

    /// <summary>
    /// A name is letters and spaces only (Arabic or Latin), 2 to 8 words. Anything else (digits,
    /// a question, a sentence) is not taken as a name.
    /// </summary>
    public static bool TryParse(string? text, out PersonName name)
    {
        name = new PersonName("", "", 0);
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return false;
        }

        var normalized = Normalize(text);
        if (!LettersOnly().IsMatch(normalized))
        {
            return false;
        }

        var words = normalized.Split(' ');
        if (words.Length is < 2 or > 8)
        {
            return false;
        }

        var parts = 0;
        for (var i = 0; i < words.Length; i++)
        {
            parts++;
            if (JoinsNextWord.Contains(words[i]) && i + 1 < words.Length)
            {
                i++;
            }
        }

        name = new PersonName(text.Trim(), normalized, parts);
        return true;
    }

    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            // Tashkeel (U+064B..U+0652), superscript alef (U+0670) and tatweel (U+0640) go.
            if (c is >= 'ً' and <= 'ْ' or 'ٰ' or 'ـ')
            {
                continue;
            }

            sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    // Arabic letters only (not Arabic punctuation such as ؟ or ، nor Arabic-Indic digits).
    [GeneratedRegex(@"^[ء-غف-يٱ-ۓA-Za-z ]+$")]
    private static partial Regex LettersOnly();

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();
}

/// <summary>An amount in Egyptian pounds (B-23 prices are whole pounds).</summary>
public readonly record struct Money(decimal Pounds)
{
    public static Money Zero => new(0);

    public static Money Of(decimal pounds) =>
        pounds < 0 ? throw new ArgumentOutOfRangeException(nameof(pounds), "an amount cannot be negative") : new Money(pounds);

    public static Money operator -(Money a, Money b) => new(a.Pounds - b.Pounds);

    public static bool operator <(Money a, Money b) => a.Pounds < b.Pounds;

    public static bool operator >(Money a, Money b) => a.Pounds > b.Pounds;

    public static bool operator <=(Money a, Money b) => a.Pounds <= b.Pounds;

    public static bool operator >=(Money a, Money b) => a.Pounds >= b.Pounds;
}
