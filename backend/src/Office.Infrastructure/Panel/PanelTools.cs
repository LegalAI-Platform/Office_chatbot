using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Office.Application.Common;
using Office.Domain.Common;
using Office.Infrastructure.Lists;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Panel;

// TASK 07c (DECISIONS v1.11): the panel's tools for the manager and the lawyer. The office's lists
// (B-14), muted conversations (S-01, the manager only), Hazem's approved texts with every version
// kept, and the month's reports. Every change is written to hazem.panel_audit (codes and ids, never
// a client's words).

public sealed record ListEntryView(Guid Id, string Kind, string WaId, string? Name, string? StaffName, string? Note, DateTimeOffset ImportedAt);

public sealed record MutedView(string WaId, string? Name, DateTimeOffset Since);

public sealed record TextView(string Key, int Version, string Text, int MaxChars, string Source, DateTimeOffset UpdatedAt, string? UpdatedBy,
    int Versions);

public sealed record TextVersionView(int Version, string Text, bool Active, string Source, DateTimeOffset UpdatedAt, string? UpdatedBy);

public sealed record ReportRow(string Key, int Count);

public sealed record StaffReportRow(string Staff, int Assigned, int Done, int OnTime);

/// <summary>
/// The owner's request (2026-11-07, «جاله كام مكالمة وكل مكالمة سعرها كام»): one service of the month. <see cref="Booked"/> its
/// appointments or orders paid for (confirmed, completed, not attended; an order priced), <see cref="Completed"/> the ones done
/// (completed; an order delivered), <see cref="Received"/> what came and was not given back, <see cref="Price"/> the price it was
/// booked at (null for the written services, priced one by one).
/// </summary>
public sealed record ServiceReportRow(string Key, int Booked, int Completed, int Received, int? Price);

/// <summary>
/// TASK 09: <see cref="ByBooking"/> the month's appointments by state (S-03, the holds that ran out too);
/// the money of those appointments as the office marked it: <see cref="Received"/> what came and was not given back,
/// <see cref="Missing"/> what is still missing on «ناقص», and <see cref="Refunded"/> what was given back this month.
/// </summary>
public sealed record MonthReport(string Month, int Tasks, int Done, int DoneOnTime, int Late, IReadOnlyList<ReportRow> ByType,
    IReadOnlyList<StaffReportRow> ByStaff, int NewClients, IReadOnlyList<ReportRow> BySource, IReadOnlyList<ReportRow> ByMatter,
    IReadOnlyList<ReportRow> ByClientType, IReadOnlyList<ReportRow> ByBooking, int Received, int Missing, int Refunded,
    RatingReport Ratings)
{
    /// <summary>v1.39 (TASK 16, 3.4): «محادثات محتاجة مراجعة», the month's turns flagged by the system.</summary>
    public IReadOnlyList<ReviewRow> Review { get; init; } = [];

    /// <summary>«حسب الخدمة»: each service's bookings, the ones done, and its money this month.</summary>
    public IReadOnlyList<ServiceReportRow> ByService { get; init; } = [];

    /// <summary>v1.53 (B-34, layer 6): «ردود حازم الحرة», each free answer of the month, sent or refused and why.</summary>
    public IReadOnlyList<FreeAnswerRow> FreeAnswers { get; init; } = [];

    /// <summary>v1.56 (B-34): how many replies Hazem wrote this month, how many went back to the approved words, and why.</summary>
    public WrittenSummary Written { get; init; } = new(0, 0, 0, []);

    /// <summary>v1.58: what the AI cost this month (hazem.ai_usage).</summary>
    public AiCostSummary AiCost { get; init; } = new(0m, 0, 0, 0, 0, 0);
}

/// <summary>
/// v1.58 (the owner: «المحادثة صرفت قد إيه من الكريدت؟»): the month's AI spend in USD, its calls, the clients it was spent on and
/// the tokens. <see cref="Unpriced"/>: calls of a model with no known price (their tokens count, their cost does not).
/// </summary>
public sealed record AiCostSummary(decimal TotalUsd, int Calls, int Conversations, long InputTokens, long OutputTokens, int Unpriced);

/// <summary>v1.58: one client's conversation in the month: its turns (messages answered), calls, tokens and cost.</summary>
public sealed record AiCostConversation(Guid PersonId, string? Client, int Turns, int Calls, long InputTokens, long OutputTokens,
    decimal CostUsd, DateTimeOffset LastAt, int Unpriced);

/// <summary>v1.58: the month's AI cost, by conversation, the dearest first (100 at most).</summary>
public sealed record AiCostReport(string Month, AiCostSummary Summary, IReadOnlyList<AiCostConversation> Conversations);

/// <summary>v1.58: one AI call of a message: what it was for (classify, write, review), the model, its tokens and its cost.</summary>
public sealed record AiCostCall(string Kind, string Model, int InputTokens, int CacheReadTokens, int CacheWriteTokens, int OutputTokens,
    decimal? CostUsd);

/// <summary>v1.58: one message of a conversation (a turn): when, what it cost in all, and its calls. Never the words of the chat.</summary>
public sealed record AiCostTurn(DateTimeOffset At, decimal CostUsd, long InputTokens, long OutputTokens, IReadOnlyList<AiCostCall> Calls);

/// <summary>
/// v1.56: the month's written replies (every purpose): <see cref="Total"/>, <see cref="Fallbacks"/> (a check refused it and the
/// approved words went), <see cref="Retried"/> (written a second time after a refusal) and the reasons of the fallbacks.
/// </summary>
public sealed record WrittenSummary(int Total, int Fallbacks, int Retried, IReadOnlyList<ReportRow> ByReason);

/// <summary>
/// v1.53 (B-34, layer 6): one free answer: when, the client (as in <see cref="ReviewRow"/>), Hazem's words when they went,
/// else null, and the guard: «passed», or why the approved words went instead (a listed word, a number not given, the
/// reviewer's «review_promise»…). Hazem's words only, never the client's.
/// </summary>
public sealed record FreeAnswerRow(string Purpose, DateTimeOffset At, string? Client, string? Text, string Guard);

/// <summary>
/// v1.39 (TASK 16, 3.4): one conversation to review: when, the client (his name as the office sees it, else the last two
/// digits of his key), and why: repeated (Hazem had to say «معلش لو مش واضح»), unclear_twice (two unclear in a row),
/// asked_status («فاكرني؟»، «ميعادي امتى؟»), offer_after_payment (a paid offer to a client who had paid: the alarm,
/// it should never happen), left_after_price (the price or the offer was the last thing he got, a day ago, and he never
/// booked).
/// </summary>
public sealed record ReviewRow(DateTimeOffset At, string? Client, string Reason);

/// <summary>v1.31 (B-26): one service's ratings that month: how many scores and their average (2 to 5).</summary>
public sealed record RatingServiceRow(string Key, int Count, double Average);

/// <summary>v1.31 (B-26): a rating «يحتاج للتحسين»: when it was answered, the service, and the client's name as written.</summary>
public sealed record LowRatingRow(DateTimeOffset At, string Service, string? ClientName);

/// <summary>
/// v1.31 (B-26) «تقييمات العملاء»: the ratings sent in the month (<see cref="Sent"/>), how many of them got a score
/// (<see cref="Answered"/>; «ليس الآن» is no score), the average score (null: none yet), by service, and the low ones.
/// </summary>
public sealed record RatingReport(int Sent, int Answered, double? Average, IReadOnlyList<RatingServiceRow> ByService,
    IReadOnlyList<LowRatingRow> Low);

public sealed partial class PanelTools(HazemDbContext db, ListImporter importer, TimeProvider clock)
{
    [System.Text.RegularExpressions.GeneratedRegex(@"\{\{[a-z_]+\}\}")]
    private static partial System.Text.RegularExpressions.Regex SlotRule();

    public const string ErrInvalidPhone = "invalid_phone";
    public const string ErrInvalidKind = "invalid_kind";
    public const string ErrTooLong = "too_long";
    public const string ErrPlaceholder = "placeholder";
    public const string ErrUnknownText = "unknown_text";
    public const string ErrUnknownStaff = "unknown_staff";

    // ---------- lists (B-14) ----------

    public async Task<(IReadOnlyList<ListEntryView> Items, int Total)> ListsAsync(string? kind, string? q, int page, CancellationToken ct)
    {
        var rows = from e in db.ListEntries.AsNoTracking()
                   join s in db.Staff.AsNoTracking() on e.StaffId equals s.Id into ss
                   from s in ss.DefaultIfEmpty()
                   select new { e, StaffName = s == null ? null : s.Name };
        if (kind is { Length: > 0 })
        {
            rows = rows.Where(x => x.e.ListKind == kind);
        }

        if (q is { Length: > 0 })
        {
            var digits = new string(q.Where(char.IsDigit).ToArray());
            rows = rows.Where(x => (x.e.Name != null && x.e.Name.Contains(q)) || (digits.Length >= 3 && x.e.WaId.EndsWith(digits)));
        }

        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.e.ListKind).ThenBy(x => x.e.Name).Skip((page - 1) * 50).Take(50).ToListAsync(ct);
        return (items.Select(x => new ListEntryView(x.e.Id, x.e.ListKind, x.e.WaId, x.e.Name, x.StaffName, x.e.Note, x.e.ImportedAt)).ToList(), total);
    }

    public async Task<string?> AddToListAsync(Guid by, string? kind, string? phone, string? name, string? staffName, string? note, CancellationToken ct)
    {
        if (kind is null || !ListImporter.Kinds.Contains(kind))
        {
            return ErrInvalidKind;
        }

        if (!PhoneNumbers.TryToWaId(phone ?? "", out var waId))
        {
            return ErrInvalidPhone;
        }

        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (name?.Length > 120 || note?.Length > 500)
        {
            return ErrTooLong;
        }

        // One of the office's staff, picked from the list (a typo never makes a new staff member;
        // the CSV import is where new names come from).
        Guid? staffId = null;
        if (kind == "existing_client" && !string.IsNullOrWhiteSpace(staffName))
        {
            var staff = staffName.Trim();
            staffId = await db.Staff.Where(s => s.Name == staff && s.Active).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (staffId is null)
            {
                return ErrUnknownStaff;
            }
        }

        var id = await db.Database.SqlQuery<Guid>($"""
            INSERT INTO hazem.list_entries (list_kind, wa_id, name, staff_id, note, import_batch)
            VALUES ({kind}, {waId}, {name}, {staffId}, {note}, gen_random_uuid())
            ON CONFLICT (list_kind, wa_id) DO UPDATE SET name = EXCLUDED.name, staff_id = EXCLUDED.staff_id, note = EXCLUDED.note,
                                                         imported_at = now()
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        await Audit(by, "list.add", "list", id.Single(), new() { ["kind"] = kind }, ct);
        return null;
    }

    public async Task<bool> RemoveFromListAsync(Guid by, Guid id, CancellationToken ct)
    {
        var kind = await db.ListEntries.Where(e => e.Id == id).Select(e => e.ListKind).FirstOrDefaultAsync(ct);
        if (kind is null)
        {
            return false;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM hazem.list_entries WHERE id = {id}", ct);
        await Audit(by, "list.remove", "list", id, new() { ["kind"] = kind }, ct);
        return true;
    }

    public async Task<ListImporter.Result> ImportAsync(Guid by, string kind, string csv, bool replace, CancellationToken ct)
    {
        var r = await importer.ImportAsync(kind, new StringReader(csv), replace, ct);
        await Audit(by, "list.import", "list", null, new()
        {
            ["kind"] = kind, ["replace"] = replace ? "true" : "false",
            ["imported"] = r.Imported.ToString(CultureInfo.InvariantCulture), ["rejected"] = r.Rejected.Count.ToString(CultureInfo.InvariantCulture),
        }, ct);
        return r;
    }

    // ---------- muted conversations (S-01) ----------

    public async Task<IReadOnlyList<MutedView>> MutedAsync(CancellationToken ct) =>
        await (from c in db.Conversations.AsNoTracking()
               join p in db.Persons.AsNoTracking() on c.PersonId equals p.Id
               where c.State == "muted"
               orderby c.UpdatedAt descending
               select new MutedView(p.WaId, p.OfficeName, c.UpdatedAt)).Take(200).ToListAsync(ct);

    public Task AuditUnmuteAsync(Guid by, string waId, CancellationToken ct) =>
        Audit(by, "conversation.unmute", "conversation", null, new() { ["wa_id"] = LogMask.Phone(waId) }, ct);

    // ---------- approved texts ----------

    public async Task<IReadOnlyList<TextView>> TextsAsync(CancellationToken ct)
    {
        var rows = await db.ApprovedTexts.AsNoTracking().ToListAsync(ct);
        return rows.GroupBy(t => t.Key).Select(g =>
        {
            var active = g.FirstOrDefault(t => t.Active) ?? g.OrderByDescending(t => t.Version).First();
            return new TextView(g.Key, active.Version, active.Text, active.MaxChars, active.Source, active.CreatedAt, active.UpdatedBy, g.Count());
        }).OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<TextVersionView>> HistoryAsync(string key, CancellationToken ct) =>
        await db.ApprovedTexts.AsNoTracking().Where(t => t.Key == key).OrderByDescending(t => t.Version)
            .Select(t => new TextVersionView(t.Version, t.Text, t.Active, t.Source, t.CreatedAt, t.UpdatedBy)).ToListAsync(ct);

    /// <summary>
    /// A new version, active at once; the old one stays and can be brought back. B-21's limit holds
    /// (300 or 600 characters, as the text was approved). Hazem's own rule for an unfilled slot
    /// (TextRenderer.HasOpenSlot: {{…}} or […]) holds too, except the slots the approved text already
    /// had ({{media}}, filled when it is sent): otherwise Hazem would silently stop sending it.
    /// </summary>
    public async Task<(string? Error, int Version)> SaveTextAsync(Guid by, string username, string key, string? text, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // One change of a key at a time (two people saving at once would both take version N+1).
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"approved_text:" + key}))", ct);
        var current = await db.ApprovedTexts.AsNoTracking().Where(t => t.Key == key).OrderByDescending(t => t.Version).FirstOrDefaultAsync(ct);
        var active = await db.ApprovedTexts.AsNoTracking().Where(t => t.Key == key && t.Active).Select(t => t.Text).FirstOrDefaultAsync(ct);
        if (current is null)
        {
            return (ErrUnknownText, 0);
        }

        text = (text ?? "").Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Length == 0 || text.Length > current.MaxChars)
        {
            return (ErrTooLong, 0);
        }

        var allowed = SlotRule().Matches(active ?? current.Text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        if (Office.Application.Texts.TextRenderer.HasOpenSlot(SlotRule().Replace(text, m => allowed.Contains(m.Value) ? "" : m.Value)))
        {
            return (ErrPlaceholder, 0);
        }

        var version = current.Version + 1;
        // Only the active flag changes on the old row: who wrote it stays.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.approved_texts SET active = false WHERE key = {key} AND active", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by)
            VALUES ({key}, {version}, {text}, {current.MaxChars}, true, {"panel " + clock.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, {username})
            """, ct);
        await tx.CommitAsync(ct);
        await Audit(by, "text.save", "text", null, new() { ["key"] = key, ["version"] = version.ToString(CultureInfo.InvariantCulture) }, ct);
        return (null, version);
    }

    /// <summary>Brings back an earlier version (it becomes the active one; nothing is deleted). Who brought it back is in the audit.</summary>
    public async Task<string?> ActivateTextAsync(Guid by, string username, string key, int version, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"approved_text:" + key}))", ct);
        if (!await db.ApprovedTexts.AnyAsync(t => t.Key == key && t.Version == version, ct))
        {
            return ErrUnknownText;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.approved_texts SET active = false WHERE key = {key} AND active", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE hazem.approved_texts SET active = true WHERE key = {key} AND version = {version}", ct);
        await tx.CommitAsync(ct);
        await Audit(by, "text.activate", "text", null, new() { ["key"] = key, ["version"] = version.ToString(CultureInfo.InvariantCulture) }, ct);
        return null;
    }

    // ---------- reports ----------

    /// <summary>
    /// One Cairo month: tasks by type and by staff member (done, and done before their due time),
    /// and new clients by source (B-13), topic (B-25) and kind. «On time» = finished before due_at.
    /// </summary>
    public async Task<MonthReport> ReportAsync(int year, int month, CancellationToken ct)
    {
        var cairo = Domain.Tasks.OfficeHours.Cairo;
        var start = new DateTimeOffset(new DateTime(year, month, 1), cairo.GetUtcOffset(new DateTime(year, month, 1))).ToUniversalTime();
        var nextLocal = new DateTime(year, month, 1).AddMonths(1);
        var end = new DateTimeOffset(nextLocal, cairo.GetUtcOffset(nextLocal)).ToUniversalTime();

        var tasks = await (from t in db.StaffTasks.AsNoTracking()
                           join s in db.Staff.AsNoTracking() on t.StaffId equals s.Id into ss
                           from s in ss.DefaultIfEmpty()
                           where t.CreatedAt >= start && t.CreatedAt < end
                           select new
                           {
                               t.Type, t.State, t.DueAt, Staff = s == null ? null : s.Name,
                               // When it was marked done (the audit); a later change of the row (an
                               // assignment) does not move it. Tasks done before the panel: the row's time.
                               UpdatedAt = db.PanelAuditRows.Where(a => a.TargetId == t.Id && a.Action == "task.done")
                                   .Max(a => (DateTimeOffset?)a.At) ?? t.UpdatedAt,
                           }).ToListAsync(ct);
        bool OnTime(string state, DateTimeOffset due, DateTimeOffset updated) => state == "done" && updated <= due;

        var people = await (from p in db.Persons.AsNoTracking()
                            where p.FirstSeenAt >= start && p.FirstSeenAt < end
                            select new
                            {
                                p.Source,
                                Intake = db.Intakes.Where(i => i.PersonId == p.Id).Select(i => new { i.MatterType, i.ClientType }).FirstOrDefault(),
                            }).ToListAsync(ct);

        static IReadOnlyList<ReportRow> Count(IEnumerable<string?> keys) =>
            keys.GroupBy(k => k ?? "unknown").Select(g => new ReportRow(g.Key, g.Count())).OrderByDescending(r => r.Count).ToList();

        var bookings = await (from a in db.Appointments.AsNoTracking()
                              join p in db.Payments.AsNoTracking() on (Guid?)a.Id equals p.AppointmentId into pays
                              from p in pays.DefaultIfEmpty()
                              where a.StartsAt >= start && a.StartsAt < end
                              select new
                              {
                                  a.Id, a.Service, a.Amount, a.State, Payment = p == null ? null : p.State, Due = p == null ? 0 : p.AmountDue,
                                  Came = p == null ? null : p.AmountReceived, GaveBack = p != null && p.State == "refunded" ? p.RefundDue : null,
                              })
            .ToListAsync(ct);
        // TASK 10: the service orders' payments count too, in the month they were priced.
        var orders = await (from o in db.ServiceOrders.AsNoTracking()
                            join p in db.Payments.AsNoTracking() on (Guid?)o.Id equals p.ServiceOrderId
                            where o.PricedAt >= start && o.PricedAt < end
                            select new { o.Id, o.Kind, o.State, Payment = p.State, Due = p.AmountDue, Came = p.AmountReceived, GaveBack = p.State == "refunded" ? p.RefundDue : null })
            .ToListAsync(ct);
        var refunded = await db.Payments.AsNoTracking().Where(p => p.State == "refunded" && p.RefundedAt >= start && p.RefundedAt < end)
            .SumAsync(p => p.RefundDue ?? 0, ct);
        // v1.31 (B-26): the ratings sent this month and their answers.
        var ratings = await (from r in db.ServiceRatings.AsNoTracking()
                             join p in db.Persons.AsNoTracking() on r.PersonId equals p.Id
                             where r.State == "sent" && r.SentAt >= start && r.SentAt < end
                             select new { r.Service, r.Score, r.AnsweredAt, NameRaw = p.OfficeName }).ToListAsync(ct);
        var scored = ratings.Where(r => r.Score is not null).ToList();
        static double Average(IEnumerable<short?> scores) => Math.Round(scores.Average(s => (double)s!.Value), 1);

        return new MonthReport(
            $"{year:D4}-{month:D2}",
            tasks.Count,
            tasks.Count(t => t.State == "done"),
            tasks.Count(t => OnTime(t.State, t.DueAt, t.UpdatedAt)),
            tasks.Count(t => t.State == "overdue" || (t.State == "done" && t.UpdatedAt > t.DueAt)),
            Count(tasks.Select(t => t.Type)),
            tasks.GroupBy(t => t.Staff ?? "")
                .Select(g => new StaffReportRow(g.Key, g.Count(), g.Count(t => t.State == "done"), g.Count(t => OnTime(t.State, t.DueAt, t.UpdatedAt))))
                .OrderByDescending(r => r.Assigned).ToList(),
            people.Count,
            Count(people.Select(p => p.Source)),
            Count(people.Select(p => p.Intake?.MatterType)),
            Count(people.Select(p => p.Intake?.ClientType)),
            Count(bookings.Select(b => b.State)),
            // what came and stayed: a refund given back is not income
            bookings.Sum(b => (b.Came ?? 0) - (b.GaveBack ?? 0)) + orders.Sum(o => (o.Came ?? 0) - (o.GaveBack ?? 0)),
            bookings.Where(b => b.Payment == "partial").Sum(b => b.Due - (b.Came ?? 0))
                + orders.Where(o => o.Payment == "partial").Sum(o => o.Due - (o.Came ?? 0)),
            refunded,
            new RatingReport(
                ratings.Count,
                scored.Count,
                scored.Count == 0 ? null : Average(scored.Select(r => r.Score)),
                scored.GroupBy(r => r.Service).Select(g => new RatingServiceRow(g.Key, g.Count(), Average(g.Select(r => r.Score))))
                    .OrderByDescending(r => r.Count).ToList(),
                scored.Where(r => r.Score == Application.Ratings.RatingButtons.Low).OrderByDescending(r => r.AnsweredAt)
                    .Select(r => new LowRatingRow(r.AnsweredAt!.Value, r.Service, r.NameRaw)).ToList()))
        {
            Review = await ReviewAsync(start, end, ct),
            // the money of every booking counts (one cancelled and refunded too), so the rows add up to «وصل»
            ByService = bookings.GroupBy(b => b.Service)
                .Select(g => new ServiceReportRow(g.Key, g.Where(b => b.State is "confirmed" or "completed" or "no_show").Select(b => b.Id).Distinct().Count(),
                    g.Where(b => b.State == "completed").Select(b => b.Id).Distinct().Count(),
                    g.Sum(b => (b.Came ?? 0) - (b.GaveBack ?? 0)), g.Max(b => b.Amount)))
                .Concat(orders.GroupBy(o => o.Kind)
                    .Select(g => new ServiceReportRow(g.Key, g.Select(o => o.Id).Distinct().Count(),
                        g.Where(o => o.State == "delivered").Select(o => o.Id).Distinct().Count(),
                        g.Sum(o => (o.Came ?? 0) - (o.GaveBack ?? 0)), null)))
                .Where(r => r.Booked > 0 || r.Received != 0)
                .OrderByDescending(r => r.Received).ThenByDescending(r => r.Booked).ToList(),
            FreeAnswers = await FreeAnswersAsync(start, end, ct),
            Written = await WrittenAsync(start, end, ct),
            AiCost = await AiCostSummaryAsync(start, end, ct),
        };
    }

    public const int ReviewMax = 100;

    /// <summary>The paid offers and payment requests the forbidden rule 1 keeps from a client who paid (v1.39).</summary>
    private const string PaidOffers = "'consultation_offer', 'case_offer', 'urgent_price', 'urgent_pay', 'booking_hold'";

    /// <summary>v1.39 (TASK 16, 3.4): the month's flagged turns, newest first (keys and reasons only, never a message).</summary>
    private async Task<IReadOnlyList<ReviewRow>> ReviewAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var json = await db.Database.SqlQueryRaw<string>($$"""
            WITH r AS (
              SELECT t.wa_id, t.turn_id, x.text_key, x.created_at
                FROM hazem.turn_replies x JOIN hazem.turns t ON t.turn_id = x.turn_id
               WHERE x.created_at >= {0} AND x.created_at < {1} AND x.part = 0),
            flagged AS (
              SELECT r.wa_id, min(r.created_at) AS at, 'repeated' AS reason FROM r WHERE r.text_key = 'repeat_choice' GROUP BY r.wa_id, r.turn_id
              UNION ALL
              SELECT r.wa_id, min(r.created_at), 'unclear_twice' FROM r WHERE r.text_key = 'unclear_again' GROUP BY r.wa_id, r.turn_id
              UNION ALL
              SELECT t.wa_id, c.created_at, 'asked_status' FROM hazem.classifications c JOIN hazem.turns t ON t.turn_id = c.turn_id
               WHERE c.intent = 'my_status' AND c.created_at >= {0} AND c.created_at < {1}
              UNION ALL
              SELECT r.wa_id, min(r.created_at), 'offer_after_payment' FROM r
               WHERE r.text_key IN ({{PaidOffers}})
                 AND EXISTS (SELECT 1 FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
                               JOIN hazem.persons pe ON pe.id = a.person_id
                              WHERE pe.wa_id = r.wa_id AND p.proof_at < r.created_at AND a.ends_at > r.created_at
                                AND a.state IN ('confirmed', 'completed'))
               GROUP BY r.wa_id, r.turn_id
              UNION ALL
              SELECT r.wa_id, r.created_at, 'left_after_price' FROM r
               WHERE r.text_key IN ({{PaidOffers}}, 'services_menu')
                 AND r.created_at < now() - interval '24 hours'
                 AND NOT EXISTS (SELECT 1 FROM hazem.turns l WHERE l.wa_id = r.wa_id AND l.created_at > r.created_at)
                 AND NOT EXISTS (SELECT 1 FROM hazem.persons pe JOIN hazem.appointments a ON a.person_id = pe.id
                                  WHERE pe.wa_id = r.wa_id AND a.created_at >= r.created_at)
                 AND NOT EXISTS (SELECT 1 FROM hazem.persons pe JOIN hazem.service_orders o ON o.person_id = pe.id
                                  WHERE pe.wa_id = r.wa_id AND o.created_at >= r.created_at)
                 AND r.created_at = (SELECT max(y.created_at) FROM r y WHERE y.wa_id = r.wa_id))
            SELECT coalesce(json_agg(json_build_object('at', f.at, 'client', coalesce(pe.office_name, '…' || right(f.wa_id, 2)),
                                                       'reason', f.reason) ORDER BY f.at DESC), '[]')::text AS "Value"
              FROM (SELECT DISTINCT ON (wa_id, reason, date_trunc('hour', at)) * FROM flagged ORDER BY wa_id, reason, date_trunc('hour', at), at) f
              LEFT JOIN hazem.persons pe ON pe.wa_id = f.wa_id
            """, start, end).SingleAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Take(ReviewMax)
            .Select(e => new ReviewRow(e.GetProperty("at").GetDateTimeOffset(), e.GetProperty("client").GetString(), e.GetProperty("reason").GetString()!))
            .ToList();
    }

    /// <summary>v1.53 (B-34, layer 6): the month's free answers, newest first, refused ones too (the office reviews them).</summary>
    private async Task<IReadOnlyList<FreeAnswerRow>> FreeAnswersAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var json = await db.Database.SqlQueryRaw<string>($$"""
            SELECT coalesce(json_agg(json_build_object('purpose', g.purpose, 'at', g.created_at,
                                                       'client', coalesce(pe.office_name, '…' || right(t.wa_id, 2)),
                                                       'text', g.text, 'guard', g.guard) ORDER BY g.created_at DESC), '[]')::text AS "Value"
              FROM (SELECT * FROM hazem.generated_replies
                     WHERE (purpose = 'free_answer' OR guard <> 'passed') AND created_at >= {0} AND created_at < {1}
                     ORDER BY created_at DESC LIMIT {{ReviewMax}}) g
              JOIN hazem.turns t ON t.turn_id = g.turn_id
              LEFT JOIN hazem.persons pe ON pe.wa_id = t.wa_id
            """, start, end).SingleAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(e => new FreeAnswerRow(e.GetProperty("purpose").GetString()!, e.GetProperty("at").GetDateTimeOffset(), e.GetProperty("client").GetString(),
                e.GetProperty("text").ValueKind == System.Text.Json.JsonValueKind.Null ? null : e.GetProperty("text").GetString(),
                e.GetProperty("guard").GetString()!))
            .ToList();
    }

    private static (DateTimeOffset Start, DateTimeOffset End) MonthRange(int year, int month)
    {
        var cairo = Domain.Tasks.OfficeHours.Cairo;
        var first = new DateTime(year, month, 1);
        var next = first.AddMonths(1);
        return (new DateTimeOffset(first, cairo.GetUtcOffset(first)).ToUniversalTime(), new DateTimeOffset(next, cairo.GetUtcOffset(next)).ToUniversalTime());
    }

    private static AiCostSummary ReadSummary(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var r = doc.RootElement;
        return new AiCostSummary(r.GetProperty("total").GetDecimal(), r.GetProperty("calls").GetInt32(), r.GetProperty("conversations").GetInt32(),
            r.GetProperty("input").GetInt64(), r.GetProperty("output").GetInt64(), r.GetProperty("unpriced").GetInt32());
    }

    /// <summary>v1.58: the month's AI spend (the dashboard's number).</summary>
    private async Task<AiCostSummary> AiCostSummaryAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
        ReadSummary(await db.Database.SqlQueryRaw<string>("""
            SELECT json_build_object(
                     'total', coalesce(sum(u.cost_usd), 0),
                     'calls', count(*),
                     'conversations', count(DISTINCT t.wa_id),
                     'input', coalesce(sum(u.input_tokens + u.cache_read_tokens + u.cache_write_tokens), 0),
                     'output', coalesce(sum(u.output_tokens), 0),
                     'unpriced', count(*) FILTER (WHERE u.cost_usd IS NULL))::text AS "Value"
              FROM hazem.ai_usage u JOIN hazem.turns t ON t.turn_id = u.turn_id
             WHERE u.created_at >= {0} AND u.created_at < {1}
            """, start, end).SingleAsync(ct));

    /// <summary>v1.58: the month's AI cost by conversation (the client's name as the office sees it), the dearest first.</summary>
    public async Task<AiCostReport> AiCostAsync(int year, int month, CancellationToken ct)
    {
        var (start, end) = MonthRange(year, month);
        var json = await db.Database.SqlQueryRaw<string>($$"""
            SELECT coalesce(json_agg(json_build_object('person_id', c.person_id, 'client', c.client, 'turns', c.turns, 'calls', c.calls,
                                                       'input', c.input, 'output', c.output, 'cost', c.cost, 'last_at', c.last_at,
                                                       'unpriced', c.unpriced) ORDER BY c.cost DESC), '[]')::text AS "Value"
              FROM (SELECT pe.id AS person_id, coalesce(pe.office_name, pe.name_raw, '…' || right(pe.wa_id, 2)) AS client,
                           count(DISTINCT t.turn_id) AS turns, count(*) AS calls,
                           sum(u.input_tokens + u.cache_read_tokens + u.cache_write_tokens) AS input, sum(u.output_tokens) AS output,
                           coalesce(sum(u.cost_usd), 0) AS cost, max(u.created_at) AS last_at, count(*) FILTER (WHERE u.cost_usd IS NULL) AS unpriced
                      FROM hazem.ai_usage u JOIN hazem.turns t ON t.turn_id = u.turn_id JOIN hazem.persons pe ON pe.wa_id = t.wa_id
                     WHERE u.created_at >= {0} AND u.created_at < {1}
                     GROUP BY pe.id, pe.office_name, pe.name_raw, pe.wa_id
                     ORDER BY coalesce(sum(u.cost_usd), 0) DESC LIMIT {{ReviewMax}}) c
            """, start, end).SingleAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var rows = doc.RootElement.EnumerateArray().Select(e => new AiCostConversation(e.GetProperty("person_id").GetGuid(),
            e.GetProperty("client").GetString(), e.GetProperty("turns").GetInt32(), e.GetProperty("calls").GetInt32(),
            e.GetProperty("input").GetInt64(), e.GetProperty("output").GetInt64(), e.GetProperty("cost").GetDecimal(),
            e.GetProperty("last_at").GetDateTimeOffset(), e.GetProperty("unpriced").GetInt32())).ToList();
        return new AiCostReport($"{year:0000}-{month:00}", await AiCostSummaryAsync(start, end, ct), rows);
    }

    /// <summary>v1.58: one conversation's messages in the month with the calls of each: tokens and cost (never the chat's words).</summary>
    public async Task<IReadOnlyList<AiCostTurn>> AiCostTurnsAsync(Guid personId, int year, int month, CancellationToken ct)
    {
        var (start, end) = MonthRange(year, month);
        var json = await db.Database.SqlQueryRaw<string>("""
            SELECT coalesce(json_agg(json_build_object('at', x.at, 'cost', x.cost, 'input', x.input, 'output', x.output, 'calls', x.calls)
                                     ORDER BY x.at), '[]')::text AS "Value"
              FROM (SELECT t.turn_id, min(u.created_at) AS at, coalesce(sum(u.cost_usd), 0) AS cost,
                           sum(u.input_tokens + u.cache_read_tokens + u.cache_write_tokens) AS input, sum(u.output_tokens) AS output,
                           json_agg(json_build_object('kind', u.kind, 'model', u.model, 'input', u.input_tokens, 'cache_read', u.cache_read_tokens,
                                                      'cache_write', u.cache_write_tokens, 'output', u.output_tokens, 'cost', u.cost_usd)
                                    ORDER BY u.created_at) AS calls
                      FROM hazem.ai_usage u JOIN hazem.turns t ON t.turn_id = u.turn_id JOIN hazem.persons pe ON pe.wa_id = t.wa_id
                     WHERE pe.id = {2} AND u.created_at >= {0} AND u.created_at < {1}
                     GROUP BY t.turn_id) x
            """, start, end, personId).SingleAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e => new AiCostTurn(e.GetProperty("at").GetDateTimeOffset(), e.GetProperty("cost").GetDecimal(),
            e.GetProperty("input").GetInt64(), e.GetProperty("output").GetInt64(),
            e.GetProperty("calls").EnumerateArray().Select(c => new AiCostCall(c.GetProperty("kind").GetString()!, c.GetProperty("model").GetString()!,
                c.GetProperty("input").GetInt32(), c.GetProperty("cache_read").GetInt32(), c.GetProperty("cache_write").GetInt32(),
                c.GetProperty("output").GetInt32(),
                c.GetProperty("cost").ValueKind == System.Text.Json.JsonValueKind.Null ? null : c.GetProperty("cost").GetDecimal())).ToList())).ToList();
    }

    /// <summary>v1.56: the month's written replies, how many fell back to the approved words, how many were written twice, and why.</summary>
    private async Task<WrittenSummary> WrittenAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var json = await db.Database.SqlQueryRaw<string>("""
            SELECT json_build_object(
                     'total', count(*),
                     'fallbacks', count(*) FILTER (WHERE guard <> 'passed'),
                     'retried', count(*) FILTER (WHERE prompt_version LIKE '%+retry'),
                     'reasons', coalesce((SELECT json_agg(json_build_object('key', r.guard, 'count', r.n) ORDER BY r.n DESC)
                                            FROM (SELECT guard, count(*) AS n FROM hazem.generated_replies
                                                   WHERE guard <> 'passed' AND created_at >= {0} AND created_at < {1}
                                                   GROUP BY guard) r), '[]'::json))::text AS "Value"
              FROM hazem.generated_replies WHERE created_at >= {0} AND created_at < {1}
            """, start, end).SingleAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new WrittenSummary(root.GetProperty("total").GetInt32(), root.GetProperty("fallbacks").GetInt32(), root.GetProperty("retried").GetInt32(),
            root.GetProperty("reasons").EnumerateArray().Select(e => new ReportRow(e.GetProperty("key").GetString()!, e.GetProperty("count").GetInt32())).ToList());
    }

    private Task Audit(Guid by, string action, string targetType, Guid? targetId, Dictionary<string, string?> detail, CancellationToken ct) =>
        PanelAudit.WriteAsync(db, clock, by, action, targetType, targetId, detail, ct);
}
