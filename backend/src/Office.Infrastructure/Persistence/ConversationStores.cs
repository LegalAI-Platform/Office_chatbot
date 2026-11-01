using Microsoft.EntityFrameworkCore;
using Office.Application.Conversations;
using Office.Application.Ports;
using Office.Application.Prices;
using Office.Application.Responders;
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
            p.FullNameProvided, p.PreferredCallPhone, p.Source, p.SourceRaw, p.FirstSeenAt, p.NameGender);

        var c = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(x => x.PersonId == p.Id, ct);
        var conversation = c is null
            ? Conversation.Start(person)
            : Conversation.Load(c.Id, c.PersonId, StateName.Parse<ConversationState>(c.State), c.LastStaffMessageAt, c.AbuseWarnings);

        var i = await db.Intakes.AsNoTracking().FirstOrDefaultAsync(x => x.PersonId == p.Id && x.State != "closed", ct);
        var intake = i is null
            ? Intake.Open(person)
            : Intake.Load(i.Id, i.PersonId, StateName.Parse<IntakeState>(i.State),
                i.ClientType is null ? null : StateName.Parse<ClientType>(i.ClientType), i.NameAsks,
                i.PendingIntent is null ? null : new PendingRequest(i.PendingIntent, i.PendingService, i.PendingConsultationMode),
                i.UnclearInARow, i.SenderRole, i.ExistingQuestionAsked, i.MatterType, i.MatterSubtype, i.MatterAsked);

        return new ClientState(person, conversation, intake);
    }

    public async Task SaveConversationAsync(Conversation conversation, CancellationToken ct) =>
        await SaveConversation(db, conversation, ct);

    /// <summary>
    /// Npgsql writes timestamptz only from UTC values. B-06 due times are Cairo times (+02/+03 at
    /// night: "today/tomorrow 10:00") and staff events may carry the caller's offset, so every
    /// time is converted here; the instant is unchanged.
    /// </summary>
    internal static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    internal static DateTimeOffset? Utc(DateTimeOffset? value) => value?.ToUniversalTime();

    /// <summary>Called by <see cref="TurnStore"/> inside the turn's transaction.</summary>
    internal static async Task SaveAsync(HazemDbContext db, string turnId, TurnEffects e, CancellationToken ct)
    {
        var (person, conversation, intake) = (e.State.Person, e.State.Conversation, e.State.Intake);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.persons (id, wa_id, name_raw, name_normalized, full_name_provided, preferred_call_phone,
                                       source, source_raw, first_seen_at, name_gender)
            VALUES ({person.Id}, {person.WaId.Value}, {person.Name?.Raw}, {person.Name?.Normalized}, {person.FullNameProvided},
                    {person.PreferredCallPhone}, {person.Source}, {person.SourceRaw}::jsonb, {Utc(person.FirstSeenAt)}, {person.NameGender})
            ON CONFLICT (id) DO UPDATE SET
              name_raw = EXCLUDED.name_raw, name_normalized = EXCLUDED.name_normalized,
              full_name_provided = EXCLUDED.full_name_provided, preferred_call_phone = EXCLUDED.preferred_call_phone,
              source = EXCLUDED.source, source_raw = EXCLUDED.source_raw, first_seen_at = EXCLUDED.first_seen_at,
              name_gender = EXCLUDED.name_gender
            """, ct);

        await SaveConversation(db, conversation, ct);

        // A closed request first: only one open intake per person may exist (intakes_one_open_idx).
        foreach (var closed in e.State.ClosedIntakes)
        {
            await SaveIntake(db, person, closed, ct);
        }

        await SaveIntake(db, person, intake, ct);
        if (e.Urf is { } urf)
        {
            await Office.Infrastructure.Urf.UrfStore.Write(db, urf, intake.Id, ct);
        }

        foreach (var task in e.Tasks)
        {
            // TASK 06: «write the missing office detail» is one job for the office, however many clients ask meanwhile.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.staff_tasks (person_id, intake_id, turn_id, type, staff_id, due_at)
                SELECT {person.Id}, {intake.Id}, {turnId}, {task.Kind}, {task.StaffId}, {Utc(task.DueAt)}
                 WHERE {task.Kind} <> {PendingTask.OfficeInfoMissing}
                    OR NOT EXISTS (SELECT 1 FROM hazem.staff_tasks
                                    WHERE type = {PendingTask.OfficeInfoMissing} AND state IN ('open', 'in_progress', 'overdue'))
                ON CONFLICT (turn_id, type) DO NOTHING
                """, ct);
        }

        if (e.Classification is { } c)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.classifications (turn_id, intent, confidence, service, consultation_mode, full_name_present,
                                                   model, prompt_version, latency_ms, input_tokens, output_tokens, fallback_reason,
                                                   matter_type, matter_subtype, sender_role, info_topic, scope)
                VALUES ({c.TurnId}, {c.Intent}, {(decimal)Math.Round(c.Confidence, 3)}, {c.Service}, {c.ConsultationMode}, {c.FullNamePresent},
                        {c.Model}, {c.PromptVersion}, {c.LatencyMs}, {c.InputTokens}, {c.OutputTokens}, {c.FallbackReason},
                        {c.MatterType}, {c.MatterSubtype}, {c.SenderRole}, {c.InfoTopic}, {c.Scope})
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

        if (e.Written is { } w)
        {
            // v1.41 (TASK 17, migration 0042): the written reply (or why the approved text went instead).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.generated_replies (idempotency_key, turn_id, purpose, text, guard, model, prompt_version, latency_ms,
                                                     input_tokens, output_tokens)
                VALUES ({w.IdempotencyKey}, {turnId}, {w.Purpose}, {w.Text}, {w.Guard}, {w.Model}, {w.PromptVersion}, {w.LatencyMs},
                        {w.InputTokens}, {w.OutputTokens})
                ON CONFLICT (idempotency_key) DO NOTHING
                """, ct);
        }
    }

    private static Task SaveConversation(HazemDbContext db, Conversation conversation, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.conversations (id, person_id, state, last_staff_message_at, abuse_warnings)
            VALUES ({conversation.Id}, {conversation.PersonId}, {StateName.Of(conversation.State)}, {Utc(conversation.LastStaffMessageAt)},
                    {conversation.AbuseWarnings})
            ON CONFLICT (id) DO UPDATE SET state = EXCLUDED.state, last_staff_message_at = EXCLUDED.last_staff_message_at,
              abuse_warnings = EXCLUDED.abuse_warnings
            """, ct);

    private static Task SaveIntake(HazemDbContext db, Person person, Intake intake, CancellationToken ct)
    {
        var pending = intake.Pending;
        var clientType = intake.ClientType is { } t ? StateName.Of(t) : null;
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.intakes (id, person_id, state, client_type, name_asks, pending_intent, pending_service,
                                       pending_consultation_mode, unclear_in_a_row, sender_role, existing_question_asked,
                                       matter_type, matter_subtype, matter_asked)
            VALUES ({intake.Id}, {person.Id}, {StateName.Of(intake.State)}, {clientType}, {intake.NameAsks},
                    {pending?.Intent}, {pending?.Service}, {pending?.ConsultationMode}, {intake.UnclearInARow},
                    {intake.SenderRole}, {intake.ExistingQuestionAsked}, {intake.MatterType}, {intake.MatterSubtype}, {intake.MatterAsked})
            ON CONFLICT (id) DO UPDATE SET
              state = EXCLUDED.state, client_type = EXCLUDED.client_type, name_asks = EXCLUDED.name_asks,
              pending_intent = EXCLUDED.pending_intent, pending_service = EXCLUDED.pending_service,
              pending_consultation_mode = EXCLUDED.pending_consultation_mode, unclear_in_a_row = EXCLUDED.unclear_in_a_row,
              sender_role = EXCLUDED.sender_role, existing_question_asked = EXCLUDED.existing_question_asked,
              matter_type = EXCLUDED.matter_type, matter_subtype = EXCLUDED.matter_subtype, matter_asked = EXCLUDED.matter_asked
            """, ct);
    }
}

/// <summary>
/// The active texts, with B-23's prices already in their {{price_*}} slots (TASK 06): every reply, from the flow or a worker,
/// quotes the price the panel has now, the same one the booking asks for.
/// </summary>
public sealed class ApprovedTextStore(HazemDbContext db, ISettingsReader settings) : IApprovedTexts
{
    public async Task<IReadOnlyDictionary<string, ApprovedText>> GetActiveAsync(CancellationToken ct) =>
        await GetActiveAsync(await settings.GetAsync(ct), ct);

    /// <summary>With the settings the turn already has: one read, and the texts quote the price the booking asks for.</summary>
    public async Task<IReadOnlyDictionary<string, ApprovedText>> GetActiveAsync(HazemSettings current, CancellationToken ct)
    {
        var prices = PriceList.From(current);
        return await db.ApprovedTexts.AsNoTracking().Where(t => t.Active)
            .ToDictionaryAsync(t => t.Key, t => new ApprovedText(t.Key, t.Version, prices.Fill(t.Text), t.MaxChars, t.Active), StringComparer.Ordinal, ct);
    }
}

/// <summary>B-14 lists (hazem.list_entries), imported from the office's files.</summary>
public sealed class ClientListStore(HazemDbContext db) : IClientLists
{
    public async Task<ListMatch> FindAsync(string waId, CancellationToken ct)
    {
        var rows = await db.ListEntries.AsNoTracking().Where(x => x.WaId == waId).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return ListMatch.None;
        }

        var client = rows.FirstOrDefault(r => r.ListKind == "existing_client");
        return new ListMatch(
            rows.Any(r => r.ListKind == "blocked"),
            rows.Any(r => r.ListKind == "lawyer_direct"),
            client is null ? null : new ExistingClientEntry(client.Name, client.StaffId));
    }
}
