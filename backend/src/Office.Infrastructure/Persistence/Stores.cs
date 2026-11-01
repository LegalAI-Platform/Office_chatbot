using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Conversations;
using Office.Application.Ports;
using Office.Application.Responders;
using Office.Application.Turns;
using Office.Application.UseCases;
using Office.Infrastructure.Json;

namespace Office.Infrastructure.Persistence;

public sealed class TurnStore(HazemDbContext db) : ITurnStore
{
    public const string Processed = "processed";

    public async Task<StoredTurn?> FindAsync(string turnId, CancellationToken ct)
    {
        var row = await db.Turns.AsNoTracking().FirstOrDefaultAsync(t => t.TurnId == turnId, ct);
        return row is null
            ? null
            : new StoredTurn(
                row.TurnId,
                row.WaId,
                row.ContactName,
                JsonSerializer.Deserialize<List<IncomingMessage>>(row.Messages, HazemJson.Options) ?? [],
                row.Responder,
                JsonSerializer.Deserialize<List<OutboundAction>>(row.Actions, HazemJson.Options) ?? []);
    }

    public async Task<bool> TryAddAsync(StoredTurn turn, TurnEffects? effects, CancellationToken ct)
    {
        var messages = JsonSerializer.Serialize(turn.Messages, HazemJson.Options);
        var actions = JsonSerializer.Serialize(turn.Actions, HazemJson.Options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // ON CONFLICT instead of catching a unique violation: two identical requests at the
        // same moment both stay healthy, and exactly one of them inserts. The loser waits for the
        // winner's commit here, then inserts nothing and saves none of its effects.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.turns (turn_id, wa_id, contact_name, messages, status, responder, actions)
            VALUES ({turn.TurnId}, {turn.WaId}, {turn.ContactName}, {messages}::jsonb, {Processed}, {turn.Responder}, {actions}::jsonb)
            ON CONFLICT (turn_id) DO NOTHING
            """, ct);
        if (inserted != 1)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        if (effects is not null)
        {
            await ConversationStore.SaveAsync(db, turn.TurnId, effects, ct);
        }

        if (turn.ContactName is not null)
        {
            // v1.36 (migration 0038): his WhatsApp profile name, for the office only (cleaned, 60 characters at most).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE hazem.persons SET whatsapp_name = n.name
                  FROM (SELECT left(btrim(regexp_replace({turn.ContactName}, '[[:cntrl:]]', '', 'g')), 60) AS name) n
                 WHERE wa_id = {turn.WaId} AND n.name <> '' AND whatsapp_name IS DISTINCT FROM n.name
                """, ct);
        }

        if (turn.ContactUsername is { Length: > 0 and <= 64 } username)
        {
            // v1.37 (TASK 15, migration 0039): his WhatsApp username («@…» in the test numbers and the testing tools).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE hazem.persons SET whatsapp_username = {username}
                 WHERE wa_id = {turn.WaId} AND whatsapp_username IS DISTINCT FROM {username}
                """, ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }
}

public sealed class SettingsReader(HazemDbContext db) : ISettingsReader
{
    public async Task<HazemSettings> GetAsync(CancellationToken ct) =>
        new(await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, ct));
}

public sealed class SendResultStore(HazemDbContext db, TimeProvider clock) : ISendResultStore
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

        // v1.44: a reply of Hazem's that did not reach the client in the end (n8n already tried it again) is a task for the
        // office, once per turn: «رسالة ما وصلتش للعميل». The client's words are in the turn; the office answers him.
        if (turnId is not null && reportedBy == RecordSendResults.ReportedByN8n && results.Any(r => !r.Ok))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.staff_tasks (person_id, turn_id, type, due_at)
                SELECT p.id, {turnId}, {PendingTask.BookingMessageFailed}, {Office.Domain.Tasks.OfficeHours.DueAt(clock.GetUtcNow()).ToUniversalTime()}
                  FROM hazem.turns t JOIN hazem.persons p ON p.wa_id = t.wa_id
                 WHERE t.turn_id = {turnId}
                ON CONFLICT (turn_id, type) DO NOTHING
                """, ct);
        }

        return written;
    }
}
