using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Core.Hazem;

namespace Office.Infrastructure.Persistence;

public sealed class TurnStore(HazemDbContext db) : ITurnStore
{
    public async Task<StoredTurn?> FindAsync(string turnId, CancellationToken ct)
    {
        var row = await db.Turns.AsNoTracking().FirstOrDefaultAsync(t => t.TurnId == turnId, ct);
        return row is null
            ? null
            : new StoredTurn(
                row.TurnId,
                row.WaId,
                row.ContactName,
                JsonSerializer.Deserialize<List<TurnMessage>>(row.Messages, HazemJson.Options) ?? [],
                row.Status,
                row.Responder,
                JsonSerializer.Deserialize<List<TurnAction>>(row.Actions, HazemJson.Options) ?? []);
    }

    public async Task<bool> TryAddAsync(StoredTurn turn, CancellationToken ct)
    {
        var messages = JsonSerializer.Serialize(turn.Messages, HazemJson.Options);
        var actions = JsonSerializer.Serialize(turn.Actions, HazemJson.Options);
        // ON CONFLICT instead of catching a unique violation: two identical requests at the
        // same moment both stay healthy, and exactly one of them inserts.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.turns (turn_id, wa_id, contact_name, messages, status, responder, actions)
            VALUES ({turn.TurnId}, {turn.WaId}, {turn.ContactName}, {messages}::jsonb, {turn.Status}, {turn.Responder}, {actions}::jsonb)
            ON CONFLICT (turn_id) DO NOTHING
            """, ct);
        return inserted == 1;
    }
}

public sealed class SettingsReader(HazemDbContext db) : ISettingsReader
{
    public async Task<HazemSettings> GetAsync(CancellationToken ct) =>
        new(await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, ct));
}

public sealed class SendResultStore(HazemDbContext db) : ISendResultStore
{
    public async Task<int> UpsertAsync(string? turnId, IReadOnlyList<SendResult> results, string reportedBy, CancellationToken ct)
    {
        var written = 0;
        foreach (var r in results)
        {
            written += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.send_results
                  (idempotency_key, turn_id, ok, outbound_id, wamid, status, error_code, error_message, reported_by)
                VALUES ({r.IdempotencyKey}, {turnId}, {r.Ok}, {r.OutboundId}, {r.Wamid}, {r.Status}, {r.ErrorCode}, {r.ErrorMessage}, {reportedBy})
                ON CONFLICT (idempotency_key) DO UPDATE SET
                  turn_id = COALESCE(EXCLUDED.turn_id, send_results.turn_id),
                  ok = EXCLUDED.ok, outbound_id = EXCLUDED.outbound_id, wamid = EXCLUDED.wamid,
                  status = EXCLUDED.status, error_code = EXCLUDED.error_code,
                  error_message = EXCLUDED.error_message, reported_by = EXCLUDED.reported_by
                """, ct);
        }

        return written;
    }
}
