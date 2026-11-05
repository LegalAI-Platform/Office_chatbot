using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Services;
using Office.Application.Urf;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Urf;

/// <summary>hazem.urf_cases and hazem.urf_files (migration 0016).</summary>
public sealed class UrfStore(HazemDbContext db) : IUrfStore
{
    public async Task<IReadOnlyList<UrfCase>> OpenAsync(Guid personId, CancellationToken ct) =>
        (await db.UrfCases.AsNoTracking()
            .Where(c => c.PersonId == personId && (c.State == UrfCase.Collecting || c.State == UrfCase.DocumentsPending || c.State == UrfCase.Booked))
            .OrderByDescending(c => c.CreatedAt).ToListAsync(ct)).Select(ToCase).ToList();

    public Task SaveAsync(UrfCase c, Guid? intakeId, CancellationToken ct) => Write(db, c, intakeId, ct);

    /// <summary>TASK 18: also called inside the turn's transaction, after the person and the intake it points to are written.</summary>
    internal static Task Write(HazemDbContext db, UrfCase c, Guid? intakeId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.urf_cases (id, person_id, intake_id, kind, state, step, husband_age, wife_age, wife_past, guardian, witnesses,
                                         has_copy, agreed, children, age_tries, appointment_id, policy_version)
            VALUES ({c.Id}, {c.PersonId}, {intakeId}, {c.Kind}, {c.State}, {c.Step}, {c.HusbandAge}, {c.WifeAge}, {c.WifePast}, {c.Guardian},
                    {c.Witnesses}, {c.HasCopy}, {c.Agreed}, {c.Children}, {c.AgeTries}, {c.AppointmentId}, {c.PolicyVersion})
            ON CONFLICT (id) DO UPDATE SET
              state = EXCLUDED.state, step = EXCLUDED.step, husband_age = EXCLUDED.husband_age, wife_age = EXCLUDED.wife_age,
              wife_past = EXCLUDED.wife_past, guardian = EXCLUDED.guardian, witnesses = EXCLUDED.witnesses, has_copy = EXCLUDED.has_copy,
              agreed = EXCLUDED.agreed, children = EXCLUDED.children, age_tries = EXCLUDED.age_tries,
              appointment_id = EXCLUDED.appointment_id, policy_version = EXCLUDED.policy_version
             -- a decision of the panel (approved, refused) or of the worker in between is never written over
             WHERE hazem.urf_cases.state IN ('collecting', 'documents_pending', 'booked')
            """, ct);

    public async Task<AgeRefusal?> AgeRefusalAsync(Guid personId, DateTimeOffset since, CancellationToken ct)
    {
        // The case's updated_at is the moment it was refused (a refused case is never written again); the closing texts are
        // the turn replies of this client after it, and any other reply since means he moved on. v1.71: the guardian too.
        var json = await db.Database.SqlQuery<string?>($"""
            WITH after AS (
              SELECT r.text_key FROM hazem.turn_replies r
                JOIN hazem.turns t ON t.turn_id = r.turn_id
                JOIN hazem.persons p ON p.wa_id = t.wa_id
               WHERE p.id = {personId} AND r.created_at > (SELECT max(updated_at) FROM hazem.urf_cases
                                                            WHERE person_id = {personId} AND state IN ('rejected_age', 'rejected_guardian')))
            SELECT json_build_object('at', c.updated_at,
                     'state', c.state,
                     'id', c.id, 'kind', c.kind, 'husband_age', c.husband_age, 'wife_age', c.wife_age, 'wife_past', c.wife_past,
                     'guardian', c.guardian, 'witnesses', c.witnesses, 'has_copy', c.has_copy, 'agreed', c.agreed,
                     'children', c.children, 'policy_version', c.policy_version,
                     'replies', (SELECT count(*) FROM after WHERE text_key IN ('urf_age_closed', 'urf_guardian_closed')),
                     'moved_on', EXISTS (SELECT 1 FROM after WHERE text_key NOT IN
                                         ('urf_age_closed', 'urf_under_age', 'urf_guardian_closed', 'urf_no_guardian')))::text AS "Value"
              FROM hazem.urf_cases c
             WHERE c.person_id = {personId} AND c.state IN ('rejected_age', 'rejected_guardian') AND c.updated_at > {since}
               -- v1.72: a case opened after it (the refusal changed by what he said) ends it
               AND NOT EXISTS (SELECT 1 FROM hazem.urf_cases n WHERE n.person_id = c.person_id AND n.created_at > c.updated_at)
             ORDER BY c.updated_at DESC
             LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (json is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        static string? Text(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        static int? Int(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null;
        static bool? Flag(JsonElement e) => e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : null;
        var state = r.GetProperty("state").GetString()!;
        var refused = new UrfCase(r.GetProperty("id").GetGuid(), personId, r.GetProperty("kind").GetString()!, state, null,
            Int(r.GetProperty("husband_age")), Int(r.GetProperty("wife_age")), Text(r.GetProperty("wife_past")), Text(r.GetProperty("guardian")),
            Text(r.GetProperty("witnesses")), Flag(r.GetProperty("has_copy")), Flag(r.GetProperty("agreed")), Flag(r.GetProperty("children")),
            PolicyVersion: Text(r.GetProperty("policy_version")));
        return new AgeRefusal(r.GetProperty("at").GetDateTimeOffset(), r.GetProperty("replies").GetInt32(), r.GetProperty("moved_on").GetBoolean(),
            state, refused);
    }

    public async Task<AddFileOutcome> AddFileAsync(Guid personId, StoredFile f, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await db.Database.SqlQuery<string>($"""
            SELECT hazem.add_urf_file({personId}, {f.Wamid}, {f.Bucket}, {f.Key}, {f.MimeType}, {f.Filename})::text AS "Value"
            """).SingleAsync(ct));
        var r = doc.RootElement;
        return new AddFileOutcome(r.GetProperty("status").GetString() switch
        {
            "first" => AddFileStatus.First,
            "more" => AddFileStatus.More,
            "exists" => AddFileStatus.Exists,
            _ => AddFileStatus.None,
        }, r.TryGetProperty("id", out var id) ? id.GetGuid() : null, null);
    }

    internal static UrfCase ToCase(UrfCaseRow r) => new(r.Id, r.PersonId, r.Kind, r.State, r.Step, r.HusbandAge, r.WifeAge, r.WifePast,
        r.Guardian, r.Witnesses, r.HasCopy, r.Agreed, r.Children, r.AgeTries, r.AppointmentId, r.PolicyVersion);
}
