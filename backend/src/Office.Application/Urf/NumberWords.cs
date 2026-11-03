using System.Text;

namespace Office.Application.Urf;

/// <summary>
/// v1.52 (phase 1): whole numbers from 1 to 99 written in Arabic words, Egyptian or standard («تلاتين»، «خمسة وعشرين»،
/// «سبعتاشر»، «سبعة عشر»، «ثلاثين»), turned into digits so the ages are read by code as typed digits are; «سبعتاشر» must
/// be 17, under 18, never missed. Anything else stays as it was.
/// </summary>
public static class NumberWords
{
    private static readonly Dictionary<string, int> Units = new(StringComparer.Ordinal)
    {
        ["واحد"] = 1, ["واحده"] = 1, ["اتنين"] = 2, ["اثنين"] = 2, ["اثنان"] = 2, ["تلاته"] = 3, ["ثلاثه"] = 3, ["اربعه"] = 4,
        ["خمسه"] = 5, ["سته"] = 6, ["سبعه"] = 7, ["تمانيه"] = 8, ["ثمانيه"] = 8, ["تسعه"] = 9, ["عشره"] = 10,
    };

    /// <summary>The short units («خمس وعشرين»، «سبع عشر»): only before their tens, never alone («ست» is a woman too).</summary>
    private static readonly Dictionary<string, int> ShortUnits = new(StringComparer.Ordinal)
    {
        ["تلات"] = 3, ["ثلاث"] = 3, ["اربع"] = 4, ["خمس"] = 5, ["ست"] = 6, ["سبع"] = 7, ["تمان"] = 8, ["ثمان"] = 8, ["تسع"] = 9,
    };

    private static readonly Dictionary<string, int> Teens = new(StringComparer.Ordinal)
    {
        ["حداشر"] = 11, ["احداشر"] = 11, ["اتناشر"] = 12, ["اطناشر"] = 12, ["اثناشر"] = 12, ["تلتاشر"] = 13, ["تلطاشر"] = 13, ["ثلاثطاشر"] = 13,
        ["اربعتاشر"] = 14, ["اربعطاشر"] = 14, ["خمستاشر"] = 15, ["خمسطاشر"] = 15, ["ستاشر"] = 16, ["سطاشر"] = 16, ["سبعتاشر"] = 17,
        ["سبعطاشر"] = 17, ["تمنتاشر"] = 18, ["تمنطاشر"] = 18, ["ثمنتاشر"] = 18, ["تسعتاشر"] = 19, ["تسعطاشر"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.Ordinal)
    {
        ["عشرين"] = 20, ["تلاتين"] = 30, ["ثلاثين"] = 30, ["اربعين"] = 40, ["خمسين"] = 50, ["ستين"] = 60, ["سبعين"] = 70,
        ["تمانين"] = 80, ["ثمانين"] = 80, ["تسعين"] = 90,
    };

    /// <summary>True when the text holds at least one number word.</summary>
    public static bool Any(string? text) => text is not null && Words(text).Any(w => Value(w) is not null);

    /// <summary>The text with each number in words replaced by its digits («خمسة وعشرين» → «25»).</summary>
    public static string ToDigits(string text)
    {
        var words = Words(text);
        var output = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            var w = words[i];
            var next = i + 1 < words.Count ? words[i + 1] : null;
            if ((Units.TryGetValue(w, out var unit) || ShortUnits.TryGetValue(w, out unit)) && unit < 10 && next is not null)
            {
                // «خمسة وعشرين» / «خمسة و عشرين»: the unit and its tens
                var tens = next == "و" && i + 2 < words.Count ? words[i + 2] : next.StartsWith('و') ? next[1..] : null;
                if (tens is not null && Tens.TryGetValue(tens, out var t))
                {
                    output.Add((t + unit).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    i += next == "و" ? 2 : 1;
                    continue;
                }

                // «سبعة عشر»: the standard teens
                if (next == "عشر")
                {
                    output.Add((10 + unit).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    i++;
                    continue;
                }
            }

            output.Add(Value(w) is { } v ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : w);
        }

        return string.Join(' ', output);
    }

    private static int? Value(string w)
    {
        var bare = w.StartsWith('و') && w.Length > 2 ? w[1..] : null;
        foreach (var d in new[] { Units, Teens, Tens })
        {
            if (d.TryGetValue(w, out var v) || (bare is not null && d.TryGetValue(bare, out v)))
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>The words of the text, normalized (ة→ه، أ/إ/آ→ا، ى→ي، no diacritics or tatweel), split on anything else.</summary>
    private static List<string> Words(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                'ة' => 'ه',
                'أ' or 'إ' or 'آ' => 'ا',
                'ى' => 'ي',
                _ when ch is >= 'ً' and <= 'ٟ' or 'ـ' => '\0',
                _ when char.IsLetterOrDigit(ch) => ch,
                _ => ' ',
            });
        }

        return sb.ToString().Replace("\0", "", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
