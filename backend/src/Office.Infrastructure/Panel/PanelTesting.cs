using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Office.Application.Chatwoot;
using Office.Application.Common;
using Office.Application.Ports;
using Office.Application.Responders;
using Office.Infrastructure.Booking;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Panel;

/// <summary>One of Hazem's test numbers, as the panel shows it: never the whole number.</summary>
public sealed record TestNumberView(string Key, string Masked);

/// <summary>What forgetting a number removed.</summary>
public sealed record ForgetResult(string Masked, int Rows, int CalendarDeleted, IReadOnlyList<string> CalendarByHand, string Chatwoot,
    int ChatwootConversations);

/// <summary>
/// «أدوات التجربة» (the owner, 2026-10-30): the manager and the lawyer make Hazem forget one of his test numbers from the
/// panel, as scripts/*/forget-number does, so a conversation can be tested from the welcome again. Test environment only
/// (the endpoints answer 404 anywhere else), and only a number in hazem.recipients: a client's number can never be
/// forgotten here. Removed: every row of the number (hazem.forget_wa_id, migration 0025), its bookings' events at
/// Google, its contact and conversations in Chatwoot. Not Redis: n8n's keys there (grouping a turn) expire in seconds.
/// </summary>
public sealed class PanelTesting(HazemDbContext db, ISettingsReader settings, GoogleCalendar google, IChatwootApi chatwoot,
    TimeProvider clock, ILogger<PanelTesting> log)
{
    /// <summary>A stable key for a number, so the panel never holds the number itself.</summary>
    public static string KeyOf(string waId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("forget:" + waId)))[..16];

    public async Task<IReadOnlyList<TestNumberView>> NumbersAsync(CancellationToken ct) =>
        (await settings.GetAsync(ct)).List(HazemResponder.RecipientsKey).Order(StringComparer.Ordinal)
            .Select(w => new TestNumberView(KeyOf(w), LogMask.Phone(w))).ToList();

    /// <summary>v1.34 (B-09.6): is the urgent alert sent as a normal message inside the 24-hour window (test only)?</summary>
    public async Task<bool> UrgentWindowAsync(CancellationToken ct) =>
        (await settings.GetAsync(ct)).Flag(Application.Booking.UrgentRules.AlertInWindowKey);

    /// <summary>v1.34 (B-09.6): the switch, recorded in the audit (who, on or off).</summary>
    public async Task SetUrgentWindowAsync(Guid by, string username, bool on, CancellationToken ct)
    {
        var value = on ? "true" : "false";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.settings (key, value, description, updated_by)
            VALUES ({Application.Booking.UrgentRules.AlertInWindowKey}, {value}, 'v1.34 (B-09.6), TEST ONLY', {"panel:" + username})
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_by = EXCLUDED.updated_by
            """, ct);
        await PanelAudit.WriteAsync(db, clock, by, "testing.urgent_window", "booking", null,
            new Dictionary<string, string?> { ["on"] = value }, ct);
    }

    /// <summary>Null when the key is not one of the test numbers.</summary>
    public async Task<ForgetResult?> ForgetAsync(Guid by, string key, CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        var wa = current.List(HazemResponder.RecipientsKey).FirstOrDefault(w => KeyOf(w) == key);
        if (wa is null)
        {
            return null;
        }

        var json = await db.Database.SqlQuery<string>($"""SELECT hazem.forget_wa_id({wa})::text AS "Value" """).SingleAsync(ct);
        var result = JsonNode.Parse(json)!.AsObject();
        var rows = result["deleted"]?.AsObject().Sum(kv => (int?)kv.Value ?? 0) ?? 0;

        // Its bookings' events at Google: the database rows are gone, so a leftover event would block that time for good.
        var deleted = 0;
        var byHand = new List<string>();
        foreach (var e in result["calendar_events"]?.AsArray() ?? [])
        {
            if (await google.DeleteEventAsync(current, (string)e!["event_id"]!, ct))
            {
                deleted++;
            }
            else
            {
                byHand.Add((string)e["starts_at"]!);
            }
        }

        var chat = "off";
        var conversations = 0;
        if (chatwoot.Configured)
        {
            try
            {
                (_, conversations) = await chatwoot.DeleteContactAsync(wa, ct);
                chat = "ok";
            }
            catch (Exception ex) when (ex is ChatwootException or HttpRequestException or TaskCanceledException)
            {
                // Hazem has forgotten the number already: only the old conversation stays visible in Chatwoot.
                log.LogWarning("Forget {Wa}: Chatwoot did not delete the contact ({Error})", LogMask.Phone(wa), ex.Message);
                chat = "failed";
            }
        }

        await PanelAudit.WriteAsync(db, clock, by, "testing.forget", "conversation", null, new Dictionary<string, string?>
        {
            ["wa_id"] = LogMask.Phone(wa),
            ["rows"] = rows.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["chatwoot"] = chat,
        }, ct);
        return new ForgetResult(LogMask.Phone(wa), rows, deleted, byHand, chat, conversations);
    }
}
