using Microsoft.EntityFrameworkCore;
using Office.Application.Conversations;
using Office.Application.Texts;
using Office.Domain.Clients;
using Office.Domain.Common;

namespace Office.Infrastructure.Persistence;

public sealed class ConversationStore(HazemDbContext db) : IConversationStore
{
    public async Task<ClientState?> LoadAsync(string waId, CancellationToken ct)
    {
        var p = await db.Persons.AsNoTracking().FirstOrDefaultAsync(x => x.WaId == waId, ct);
        if (p is null)
        {
            return null;
        }

        var person = Person.Load(p.Id, WaId.From(p.WaId),
            p.NameRaw is null ? null : PersonName.Restore(p.NameRaw, p.NameNormalized), // never re-validated
            p.FullNameProvided, p.PreferredCallPhone);

        var c = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(x => x.PersonId == p.Id, ct);
        var conversation = c is null
            ? Conversation.Start(person)
            : Conversation.Load(c.Id, c.PersonId, StateName.Parse<ConversationState>(c.State), c.LastStaffMessageAt);

        var i = await db.Intakes.AsNoTracking().FirstOrDefaultAsync(x => x.PersonId == p.Id && x.State != "closed", ct);
        var intake = i is null
            ? Intake.Open(person)
            : Intake.Load(i.Id, i.PersonId, StateName.Parse<IntakeState>(i.State),
                i.ClientType is null ? null : StateName.Parse<ClientType>(i.ClientType), i.NameAsks,
                i.PendingIntent is null ? null : new PendingRequest(i.PendingIntent, i.PendingService, i.PendingConsultationMode),
                i.UnclearInARow);

        return new ClientState(person, conversation, intake);
    }

    /// <summary>Called by <see cref="TurnStore"/> inside the turn's transaction.</summary>
    internal static async Task SaveAsync(HazemDbContext db, string turnId, TurnEffects e, CancellationToken ct)
    {
        var (person, conversation, intake) = (e.State.Person, e.State.Conversation, e.State.Intake);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.persons (id, wa_id, name_raw, name_normalized, full_name_provided, preferred_call_phone)
            VALUES ({person.Id}, {person.WaId.Value}, {person.Name?.Raw}, {person.Name?.Normalized}, {person.FullNameProvided}, {person.PreferredCallPhone})
            ON CONFLICT (id) DO UPDATE SET
              name_raw = EXCLUDED.name_raw, name_normalized = EXCLUDED.name_normalized,
              full_name_provided = EXCLUDED.full_name_provided, preferred_call_phone = EXCLUDED.preferred_call_phone
            """, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.conversations (id, person_id, state, last_staff_message_at)
            VALUES ({conversation.Id}, {person.Id}, {StateName.Of(conversation.State)}, {conversation.LastStaffMessageAt})
            ON CONFLICT (id) DO UPDATE SET state = EXCLUDED.state, last_staff_message_at = EXCLUDED.last_staff_message_at
            """, ct);

        var pending = intake.Pending;
        var clientType = intake.ClientType is { } t ? StateName.Of(t) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.intakes (id, person_id, state, client_type, name_asks, pending_intent, pending_service,
                                       pending_consultation_mode, unclear_in_a_row)
            VALUES ({intake.Id}, {person.Id}, {StateName.Of(intake.State)}, {clientType}, {intake.NameAsks},
                    {pending?.Intent}, {pending?.Service}, {pending?.ConsultationMode}, {intake.UnclearInARow})
            ON CONFLICT (id) DO UPDATE SET
              state = EXCLUDED.state, client_type = EXCLUDED.client_type, name_asks = EXCLUDED.name_asks,
              pending_intent = EXCLUDED.pending_intent, pending_service = EXCLUDED.pending_service,
              pending_consultation_mode = EXCLUDED.pending_consultation_mode, unclear_in_a_row = EXCLUDED.unclear_in_a_row
            """, ct);

        foreach (var task in e.Tasks)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.pending_tasks (person_id, turn_id, kind) VALUES ({person.Id}, {turnId}, {task.Kind})
                ON CONFLICT (turn_id, kind) DO NOTHING
                """, ct);
        }

        if (e.Classification is { } c)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.classifications (turn_id, intent, confidence, service, consultation_mode, full_name_present,
                                                   model, prompt_version, latency_ms, input_tokens, output_tokens, fallback_reason)
                VALUES ({c.TurnId}, {c.Intent}, {(decimal)Math.Round(c.Confidence, 3)}, {c.Service}, {c.ConsultationMode}, {c.FullNamePresent},
                        {c.Model}, {c.PromptVersion}, {c.LatencyMs}, {c.InputTokens}, {c.OutputTokens}, {c.FallbackReason})
                ON CONFLICT (turn_id) DO NOTHING
                """, ct);
        }

        foreach (var r in e.Replies)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.turn_replies (idempotency_key, turn_id, text_key, text_version, part, instead_of, instead_reason)
                VALUES ({r.IdempotencyKey}, {turnId}, {r.Key}, {r.Version}, {r.Part}, {r.InsteadOf}, {r.Reason})
                ON CONFLICT (idempotency_key) DO NOTHING
                """, ct);
        }
    }
}

public sealed class ApprovedTextStore(HazemDbContext db) : IApprovedTexts
{
    public async Task<IReadOnlyDictionary<string, ApprovedText>> GetActiveAsync(CancellationToken ct) =>
        await db.ApprovedTexts.AsNoTracking().Where(t => t.Active)
            .ToDictionaryAsync(t => t.Key, t => new ApprovedText(t.Key, t.Version, t.Text, t.MaxChars, t.Active), StringComparer.Ordinal, ct);
}
