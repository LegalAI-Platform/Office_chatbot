using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Office.Application.Ports;
using Office.Application.Prices;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Panel;

// TASK 06 (DECISIONS v1.17, B-23): «الإعدادات ← الأسعار». Everyone signed in reads the list (the pricing screens need the
// ranges); the lawyer and the manager change it. A new price counts for new requests only: nothing stored is touched.

public sealed record PriceView(string Key, string Name, int Value, int Default);

public sealed record PriceChangeView(DateTimeOffset At, string? By, string Key, string? From, string? To);

public sealed class PanelPrices(HazemDbContext db, ISettingsReader settings, TimeProvider clock)
{
    public const string ErrInvalid = "invalid_price";

    public async Task<IReadOnlyList<PriceView>> ListAsync(CancellationToken ct)
    {
        var prices = PriceList.From(await settings.GetAsync(ct));
        return PriceList.Lines.Select(l => new PriceView(l.Key, l.Name, prices[l.Key], l.Default)).ToList();
    }

    /// <summary>The last changes, newest first, from the audit (who, when, which line, from, to).</summary>
    public async Task<IReadOnlyList<PriceChangeView>> ChangesAsync(CancellationToken ct) =>
        await db.Database.SqlQuery<PriceChangeView>($"""
            SELECT a.at, u.display_name AS by, a.detail ->> 'key' AS key, a.detail ->> 'from' AS "from", a.detail ->> 'to' AS "to"
              FROM hazem.panel_audit a LEFT JOIN hazem.panel_users u ON u.id = a.user_id
             WHERE a.target_type = 'price'
             ORDER BY a.id DESC LIMIT 50
            """).ToListAsync(ct);

    /// <summary>
    /// Saves the whole list (every line of B-23, whole pounds, the review's range the right way round). Each line that
    /// changed is recorded with its old and new value. Returns the error code, or null.
    /// </summary>
    public async Task<string?> SaveAsync(Guid by, string username, IReadOnlyDictionary<string, int>? values, CancellationToken ct)
    {
        if (values is null)
        {
            return ErrInvalid;
        }

        if (PriceList.Problem(values) is { } problem)
        {
            return problem;
        }

        var before = PriceList.From(await settings.GetAsync(ct));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var line in PriceList.Lines)
        {
            var value = values[line.Key];
            if (value == before[line.Key])
            {
                continue;
            }

            var text = value.ToString(CultureInfo.InvariantCulture);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.settings (key, value, description, updated_by) VALUES ({line.Key}, {text}, {line.Name}, {"panel:" + username})
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_by = EXCLUDED.updated_by
                """, ct);
            await PanelAudit.WriteAsync(db, clock, by, "price.changed", "price", null, new Dictionary<string, string?>
            {
                ["key"] = line.Key,
                ["from"] = before[line.Key].ToString(CultureInfo.InvariantCulture),
                ["to"] = text,
            }, ct);
        }

        await tx.CommitAsync(ct);
        return null;
    }
}
