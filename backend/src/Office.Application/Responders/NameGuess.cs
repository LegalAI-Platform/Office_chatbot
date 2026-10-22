using Office.Domain.Common;

namespace Office.Application.Responders;

/// <summary>
/// Reads a typed name when the AI did not extract one (B-02). Strict on purpose: a refusal
/// («مش هقول اسمي») or a request («عايز احجز استشارة») must never be saved as the client's name,
/// so any everyday word that is not part of a name makes it «not a name».
/// </summary>
public static class NameGuess
{
    private static readonly string[] Prefixes = ["اسمي", "إسمي", "اسمى", "انا", "أنا", "الاسم", "الإسم"];

    private static readonly HashSet<string> NotNameWords = new(StringComparer.Ordinal)
    {
        "عايز", "عاوز", "عايزة", "عاوزة", "عايزه", "عاوزه", "محتاج", "محتاجة", "مش", "لا", "لأ", "مفيش",
        "ممكن", "ازاي", "إزاي", "ليه", "امتى", "إمتى", "فين", "كام", "بكام", "هل", "ايه", "إيه",
        "احجز", "أحجز", "حجز", "استشارة", "استشاره", "ميعاد", "موعد", "سعر", "قضية", "قضيه", "محامي",
        "الاستاذ", "الأستاذ", "اسمي", "اسم", "هقول", "اقول", "أقول", "قول", "بعدين", "ضروري", "مستعجل",
        "السلام", "عليكم", "صباح", "مساء", "الخير", "شكرا", "تمام", "اوك", "ماشي", "طيب", "اهلا", "أهلا",
        "و", "في", "على", "من", "مع", "عن", "يا",
    };

    public static bool TryRead(string? text, out PersonName name)
    {
        name = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim().Trim(':', '.', '،', ' ');
        foreach (var p in Prefixes)
        {
            if (t.StartsWith(p + " ", StringComparison.Ordinal) || t.StartsWith(p + ":", StringComparison.Ordinal))
            {
                t = t[p.Length..].TrimStart(':', ' ');
                break;
            }
        }

        if (!PersonName.TryParse(t, out var parsed) || parsed.Normalized.Split(' ').Any(NotNameWords.Contains))
        {
            return false;
        }

        name = parsed;
        return true;
    }
}
