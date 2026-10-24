using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Services;
using Office.Application.Urf;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Urf;

/// <summary>hazem.urf_cases and hazem.urf_files (migration 0016).</summary>
public sealed class UrfStore(HazemDbContext db) : IUrfStore
{
    public async Task<UrfCase?> OpenAsync(Guid personId, CancellationToken ct)
    {
        var r = await db.UrfCases.AsNoTracking()
            .Where(c => c.PersonId == personId && (c.State == UrfCase.Collecting || c.State == UrfCase.DocumentsPending || c.State == UrfCase.Booked))
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        return r is null ? null : ToCase(r);
    }

    public Task SaveAsync(UrfCase c, Guid? intakeId, CancellationToken ct) =>
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
            """, ct);

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
