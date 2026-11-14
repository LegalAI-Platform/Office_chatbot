namespace Office.Application.Panel;

/// <summary>
/// v1.110 (the owner, 2026-11-14: «السيرش بار هنا حاسه بيسرش غلط» … «اعمل بحث شامل»): what was typed in the panel's search,
/// as words. A word of digits is the end of the client's number (Arabic digits, spaces, «+», «-» and brackets allowed, so
/// «٠١٠ ١٢٣» and «+20 10…» work); the whole of it a number is one such word. Every other word must be in the client's name,
/// in any order, compared through hazem.search_text (migration 0078: «احمد» finds «أحمد», «فاطمه» finds «فاطمة»).
/// </summary>
public sealed record SearchTerm(IReadOnlyList<string> Words, IReadOnlyList<string> Digits)
{
    /// <summary>Longer than this is cut (no name or number is longer).</summary>
    public const int MaxLength = 60;

    /// <summary>A number's end shorter than this matches too many numbers: left out.</summary>
    public const int MinDigits = 3;

    public bool IsEmpty => Words.Count == 0 && Digits.Count == 0;

    public static SearchTerm Parse(string? q)
    {
        var text = Latin(q ?? "").Trim();
        if (text.Length > MaxLength)
        {
            text = text[..MaxLength];
        }

        var whole = Compact(text);
        if (whole.Length > 0 && whole.All(char.IsAsciiDigit))
        {
            return new([], whole.Length >= MinDigits ? [whole] : []);
        }

        var words = new List<string>();
        var digits = new List<string>();
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var number = Compact(word);
            if (number.Length > 0 && number.All(char.IsAsciiDigit))
            {
                if (number.Length >= MinDigits)
                {
                    digits.Add(number);
                }
            }
            else
            {
                words.Add(word);
            }
        }

        return new(words, digits);
    }

    /// <summary>The number without what people put between its digits.</summary>
    private static string Compact(string s) =>
        new(s.Where(c => !char.IsWhiteSpace(c) && c is not ('+' or '-' or '(' or ')' or '.' or '‎' or '‏')).ToArray());

    /// <summary>Arabic-Indic (٠…٩) and extended (۰…۹) digits as ASCII.</summary>
    private static string Latin(string s) =>
        new(s.Select(c => c switch
        {
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            _ => c,
        }).ToArray());
}

/// <summary>A client found: the office's name for him, his number's last 3 digits, and where the panel opens him.</summary>
public sealed record SearchClient(Guid Id, string? Name, string? PhoneLast3, int OpenTasks, Guid? TaskId, DateTimeOffset? AppointmentAt);

public sealed record SearchAppointment(Guid Id, string? Name, string? PhoneLast3, string Service, string State, DateTimeOffset StartsAt);

public sealed record SearchTask(Guid Id, string? Name, string? PhoneLast3, string Type, string State, DateTimeOffset DueAt);

public sealed record SearchResults(IReadOnlyList<SearchClient> Clients, IReadOnlyList<SearchAppointment> Appointments, IReadOnlyList<SearchTask> Tasks)
{
    public static SearchResults Empty { get; } = new([], [], []);
}

public interface IPanelSearchStore
{
    /// <summary>
    /// The clients the term names, with their appointments (when <paramref name="appointments"/>) and the tasks the viewer may
    /// see (<paramref name="allTasks"/>: every one; else those of <paramref name="staffId"/> and the unassigned).
    /// </summary>
    Task<SearchResults> SearchAsync(SearchTerm term, bool allTasks, Guid? staffId, bool appointments, int each, CancellationToken ct);
}

/// <summary>v1.110: the search bar's one search: the clients, their appointments and the tasks, each as the viewer may see them.</summary>
public sealed class PanelSearch(IPanelSearchStore store)
{
    /// <summary>At most this many of each kind (the bar's list is short; the pages have the rest).</summary>
    public const int Each = 5;

    public Task<SearchResults> SearchAsync(Viewer viewer, string? q, CancellationToken ct)
    {
        var term = SearchTerm.Parse(q);
        return term.IsEmpty
            ? Task.FromResult(SearchResults.Empty)
            : store.SearchAsync(term, viewer.Can(Permissions.TasksViewAll), viewer.StaffId, viewer.Can(Permissions.ScheduleManage), Each, ct);
    }
}
