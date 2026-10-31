using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Office.Application.Panel;
using Office.Infrastructure.Json;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Panel;

/// <summary>ASP.NET Core Identity's PasswordHasher (PBKDF2, versioned format) and random secrets.</summary>
public sealed class PanelSecrets : IPanelSecrets
{
    // No 0/O, 1/l/I: the manager reads it out or copies it once.
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    private static readonly PasswordHasher<PanelUser> Hasher = new();
    private static readonly PanelUser Nobody = new()
    {
        Username = "nobody", DisplayName = "", Email = "", Role = "", PasswordHash = "", SecurityStamp = "",
    };

    private static readonly string DummyHash = Hasher.HashPassword(Nobody, RandomNumberGenerator.GetHexString(32));

    public string Hash(string password) => Hasher.HashPassword(Nobody, password);

    public (bool Ok, bool Rehash) Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(Nobody, hash, password) switch
        {
            PasswordVerificationResult.Success => (true, false),
            PasswordVerificationResult.SuccessRehashNeeded => (true, true),
            _ => (false, false),
        };

    public void VerifyDummy(string password) => Hasher.VerifyHashedPassword(Nobody, DummyHash, password);

    /// <summary>16 random characters in four groups (xxxx-xxxx-xxxx-xxxx): about 79 bits.</summary>
    public string TemporaryPassword() =>
        string.Join('-', Enumerable.Range(0, 4).Select(_ => RandomNumberGenerator.GetString(Alphabet, 4)));

    public string NewStamp() => RandomNumberGenerator.GetHexString(32, lowercase: true);
}

/// <summary>hazem.panel_users and hazem.panel_audit (migrations 0009 and 0010).</summary>
public sealed class PanelUserStore(HazemDbContext db, TimeProvider clock) : IPanelUserStore
{
    public async Task<PanelUser?> FindAsync(Guid id, CancellationToken ct) =>
        (await LoadAsync(u => u.Id == id, ct)).SingleOrDefault();

    public async Task<PanelUser?> FindByUsernameAsync(string username, CancellationToken ct) =>
        (await LoadAsync(u => u.Username == username, ct)).SingleOrDefault();

    public async Task<PanelUser?> FindByEmailAsync(string email, CancellationToken ct)
    {
        // citext compared with a text parameter is compared as text (case-sensitive): lower both sides.
        var lower = email.ToLowerInvariant();
        return (await LoadAsync(u => u.Email.ToLower() == lower, ct)).SingleOrDefault();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM hazem.panel_users WHERE id = {id} AND approval = {PanelUser.ApprovalPending}", ct);

    public async Task<IReadOnlyList<string>> ManagerEmailsAsync(CancellationToken ct) =>
        await db.PanelUsers.AsNoTracking().Where(u => u.Active && u.Role == Permissions.Manager).Select(u => u.Email).ToListAsync(ct);

    public Task SetChatwootUserIdAsync(Guid id, long chatwootUserId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE hazem.panel_users SET chatwoot_user_id = {chatwootUserId} WHERE id = {id}", ct);

    public Task<bool> EmailTakenAsync(string email, Guid? except, CancellationToken ct) =>
        // citext compared with a text parameter is compared as text (case-sensitive): lower both sides.
        db.PanelUsers.AnyAsync(u => u.Email.ToLower() == email.ToLowerInvariant() && (except == null || u.Id != except), ct);

    public async Task<IReadOnlyList<PanelUser>> ListAsync(CancellationToken ct) =>
        (await LoadAsync(_ => true, ct)).OrderByDescending(u => u.Pending).ThenByDescending(u => u.Active)
            .ThenBy(u => u.DisplayName, StringComparer.Ordinal).ToList();

    public Task<int> ActiveManagersAsync(CancellationToken ct) =>
        db.PanelUsers.CountAsync(u => u.Active && u.Role == Permissions.Manager, ct);

    public Task InsertAsync(PanelUser u, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO hazem.panel_users (id, username, display_name, email, role, staff_id, password_hash, security_stamp,
                                       must_change_password, active, approval, email_verified_at)
        VALUES ({u.Id}, {u.Username}, {u.DisplayName}, {u.Email}, {u.Role}, {u.StaffId}, {u.PasswordHash}, {u.SecurityStamp},
                {u.MustChangePassword}, {u.Active}, {u.Approval},
                {(u.Pending ? null : (u.EmailVerifiedAt ?? clock.GetUtcNow()).ToUniversalTime())})
        """, ct);

    public Task UpdateAsync(PanelUser u, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE hazem.panel_users SET display_name = {u.DisplayName}, email = {u.Email}, role = {u.Role}, staff_id = {u.StaffId},
               password_hash = {u.PasswordHash}, security_stamp = {u.SecurityStamp}, must_change_password = {u.MustChangePassword},
               failed_logins = {u.FailedLogins}, lockout_end = {u.LockoutEnd?.ToUniversalTime()}, active = {u.Active},
               last_login_at = {u.LastLoginAt?.ToUniversalTime()}, approval = {u.Approval},
               email_verified_at = {u.EmailVerifiedAt?.ToUniversalTime()}, approved_by = {u.ApprovedBy},
               approved_at = {u.ApprovedAt?.ToUniversalTime()}, totp_secret = {u.TotpSecret}, totp_enabled = {u.TotpEnabled},
               totp_last_step = {u.TotpLastStep}
         WHERE id = {u.Id}
        """, ct);

    public async Task<Guid> EnsureStaffAsync(string name, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO hazem.staff (name) VALUES ({name}) ON CONFLICT (name) DO NOTHING", ct);
        return await db.Staff.Where(s => s.Name == name).Select(s => s.Id).SingleAsync(ct);
    }

    public Task AuditAsync(Guid? userId, string action, Guid? targetUserId, IReadOnlyDictionary<string, string?> detail, CancellationToken ct) =>
        PanelAudit.WriteAsync(db, clock, userId, action, targetUserId is null ? null : "user", targetUserId, detail, ct);

    // The bytes are read only here (not in PanelUserRow): every request loads its account, never its photo.
    public async Task<PanelAvatar?> AvatarAsync(Guid id, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT json_build_object('type', avatar_type, 'data', encode(avatar, 'base64'))::text AS "Value"
              FROM hazem.panel_users WHERE id = {id} AND avatar IS NOT NULL
            """).SingleOrDefaultAsync(ct);
        if (json is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        // encode() breaks the base64 into lines; FromBase64String skips the white space.
        return new PanelAvatar(Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!),
            doc.RootElement.GetProperty("type").GetString()!);
    }

    public Task SetAvatarAsync(Guid id, PanelAvatar? avatar, DateTimeOffset? at, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.panel_users SET avatar = {avatar?.Bytes}, avatar_type = {avatar?.Type},
                   avatar_at = {(avatar is null ? null : at?.ToUniversalTime())}
             WHERE id = {id}
            """, ct);

    /// <summary>Filter first, then the staff name: EF translates this shape.</summary>
    private async Task<List<PanelUser>> LoadAsync(System.Linq.Expressions.Expression<Func<PanelUserRow, bool>> filter, CancellationToken ct)
    {
        var rows = await (from u in db.PanelUsers.AsNoTracking().Where(filter)
                          join s in db.Staff.AsNoTracking() on u.StaffId equals s.Id into ss
                          from s in ss.DefaultIfEmpty()
                          select new { u, StaffName = s == null ? null : s.Name }).ToListAsync(ct);
        return rows.Select(r => Map(r.u, r.StaffName)).ToList();
    }

    private static PanelUser Map(PanelUserRow r, string? staffName) => new()
    {
        Id = r.Id,
        Username = r.Username,
        DisplayName = r.DisplayName,
        Email = r.Email,
        Role = r.Role,
        StaffId = r.StaffId,
        StaffName = staffName,
        PasswordHash = r.PasswordHash,
        SecurityStamp = r.SecurityStamp,
        MustChangePassword = r.MustChangePassword,
        FailedLogins = r.FailedLogins,
        LockoutEnd = r.LockoutEnd,
        Active = r.Active,
        LastLoginAt = r.LastLoginAt,
        Approval = r.Approval,
        EmailVerifiedAt = r.EmailVerifiedAt,
        ApprovedBy = r.ApprovedBy,
        ApprovedAt = r.ApprovedAt,
        TotpSecret = r.TotpSecret,
        TotpEnabled = r.TotpEnabled,
        TotpLastStep = r.TotpLastStep,
        CreatedAt = r.CreatedAt,
        ChatwootUserId = r.ChatwootUserId,
        AvatarAt = r.AvatarAt,
    };
}

internal static class PanelAudit
{
    /// <summary>Who did what and when. The detail holds codes and ids only, never a password or client text.</summary>
    public static Task WriteAsync(HazemDbContext db, TimeProvider clock, Guid? userId, string action, string? targetType, Guid? targetId,
        IReadOnlyDictionary<string, string?> detail, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(detail, HazemJson.Options);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.panel_audit (at, user_id, action, target_type, target_id, detail)
            VALUES ({clock.GetUtcNow()}, {userId}, {action}, {targetType}, {targetId}, {json}::jsonb)
            """, ct);
    }
}

/// <summary>hazem.staff_tasks with the person, the intake and the turn that opened each task.</summary>
public sealed class PanelTaskStore(HazemDbContext db, TimeProvider clock) : IPanelTaskStore
{
    private static readonly string[] ActiveStates = ["open", "in_progress", "overdue"];

    public async Task<TaskList> ListAsync(TaskQuery q, Guid? onlyStaff, bool includeUnassigned, CancellationToken ct)
    {
        var rows = from t in db.StaffTasks.AsNoTracking()
                   join p in db.Persons.AsNoTracking() on t.PersonId equals p.Id
                   join s in db.Staff.AsNoTracking() on t.StaffId equals s.Id into ss
                   from s in ss.DefaultIfEmpty()
                   join i in db.Intakes.AsNoTracking() on t.IntakeId equals i.Id into ii
                   from i in ii.DefaultIfEmpty()
                   select new { t, p.NameRaw, p.WaId, StaffName = s == null ? null : s.Name, ClientType = i == null ? null : i.ClientType, Matter = i == null ? null : i.MatterType };

        if (onlyStaff is { } staff)
        {
            rows = rows.Where(x => x.t.StaffId == staff);
        }
        else if (includeUnassigned)
        {
            rows = rows.Where(x => x.t.StaffId == null);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().Replace("%", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
            var digits = new string(term.Where(char.IsAsciiDigit).ToArray());
            rows = digits.Length >= 3 && digits.Length == term.Length
                ? rows.Where(x => x.WaId.EndsWith(digits))
                : rows.Where(x => x.NameRaw != null && EF.Functions.ILike(x.NameRaw, "%" + term + "%"));
        }

        var now = clock.GetUtcNow();
        rows = q.State switch
        {
            // Late = marked overdue, or unfinished and past its due time (the overdue job may not have run yet).
            TaskStateFilter.Overdue => rows.Where(x => x.t.State == "overdue" || (ActiveStates.Contains(x.t.State) && x.t.DueAt < now)),
            TaskStateFilter.Done => rows.Where(x => x.t.State == "done"),
            TaskStateFilter.Cancelled => rows.Where(x => x.t.State == "cancelled"),
            _ => rows.Where(x => ActiveStates.Contains(x.t.State)),
        };
        var total = await rows.CountAsync(ct);
        rows = q.State is TaskStateFilter.Active or TaskStateFilter.Overdue
            ? rows.OrderBy(x => x.t.DueAt).ThenBy(x => x.t.CreatedAt)
            : rows.OrderByDescending(x => x.t.UpdatedAt);
        var page = Math.Max(1, q.Page);
        var items = await rows.Skip((page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        return new TaskList(
            items.Select(x => new TaskSummary(x.t.Id, x.t.Type, x.t.State, x.t.DueAt, x.t.CreatedAt,
                x.t.StaffId is { } sid ? new StaffRef(sid, x.StaffName!) : null,
                x.NameRaw, x.WaId[^3..], x.ClientType, x.Matter)).ToList(),
            total, page, q.PageSize);
    }

    public async Task<IReadOnlyList<DashboardTask>> DashboardRowsAsync(Guid? mineOrUnassigned, bool restricted, DateTimeOffset since, CancellationToken ct)
    {
        var start = since.ToUniversalTime();
        var rows = from t in db.StaffTasks.AsNoTracking()
                   where ActiveStates.Contains(t.State) || t.CreatedAt >= start
                   join p in db.Persons.AsNoTracking() on t.PersonId equals p.Id
                   join s in db.Staff.AsNoTracking() on t.StaffId equals s.Id into ss
                   from s in ss.DefaultIfEmpty()
                   select new { t, p.NameRaw, StaffName = s == null ? null : s.Name };
        if (restricted)
        {
            rows = mineOrUnassigned is { } mine
                ? rows.Where(x => x.t.StaffId == mine || x.t.StaffId == null)
                : rows.Where(x => x.t.StaffId == null);
        }

        var list = await rows.OrderByDescending(x => x.t.CreatedAt).Take(500).ToListAsync(ct);
        return list.Select(x => new DashboardTask(x.t.Id, x.t.Type, x.t.State, x.t.DueAt, x.t.CreatedAt, x.NameRaw, x.StaffName)).ToList();
    }

    public async Task<TaskRecord?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await db.StaffTasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null)
        {
            return null;
        }

        var person = await db.Persons.AsNoTracking().SingleAsync(p => p.Id == t.PersonId, ct);
        var conversation = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.PersonId == t.PersonId, ct);
        var intake = t.IntakeId is { } iid ? await db.Intakes.AsNoTracking().FirstOrDefaultAsync(i => i.Id == iid, ct) : null;
        var staff = t.StaffId is { } sid ? await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct) : null;
        var turn = await db.Turns.AsNoTracking().FirstOrDefaultAsync(x => x.TurnId == t.TurnId, ct);
        return new TaskRecord(t.Id, t.Type, t.State, t.DueAt, t.CreatedAt, staff is null ? null : new StaffRef(staff.Id, staff.Name),
            person.NameRaw, person.WaId, intake?.ClientType, person.Source, conversation?.State,
            intake?.MatterType, intake?.MatterSubtype, turn is null ? [] : ClientWords(turn.Messages));
    }

    public async Task<ClientFileRef?> FileAsync(Guid taskId, int index, CancellationToken ct)
    {
        var turnId = await db.StaffTasks.AsNoTracking().Where(t => t.Id == taskId).Select(t => t.TurnId).FirstOrDefaultAsync(ct);
        var messages = turnId is null ? null
            : await db.Turns.AsNoTracking().Where(t => t.TurnId == turnId).Select(t => t.Messages).FirstOrDefaultAsync(ct);
        if (messages is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(messages);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || index >= doc.RootElement.GetArrayLength())
        {
            return null;
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var m = doc.RootElement[index];
        if (!m.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object
            || Str(media, "status") != "stored" || Str(media, "bucket") is not { } bucket || Str(media, "key") is not { } key)
        {
            return null;
        }

        return new ClientFileRef(Str(m, "type") ?? "unsupported", bucket, key, Str(media, "mime_type"), Str(media, "filename"));
    }

    /// <summary>
    /// The client's words from the stored turn (n8n's shape, docs/hazem-internal-api.md): text,
    /// caption and a finished transcript, and only the type and status of a file. Never its
    /// bucket, key, file name, mime type or hash.
    /// </summary>
    internal static IReadOnlyList<ClientWord> ClientWords(string messagesJson)
    {
        using var doc = JsonDocument.Parse(messagesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var words = new List<ClientWord>();
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            string? transcript = null;
            if (m.TryGetProperty("transcript", out var tr) && tr.ValueKind == JsonValueKind.Object && Str(tr, "status") == "done")
            {
                transcript = Str(tr, "text");
            }

            string? mediaStatus = m.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object ? Str(media, "status") : null;
            DateTimeOffset? sentAt = DateTimeOffset.TryParse(Str(m, "sent_at"), out var at) ? at.ToUniversalTime() : null;
            words.Add(new ClientWord(Str(m, "type") ?? "unsupported", Str(m, "text"), Str(m, "caption"), transcript, mediaStatus, sentAt));
        }

        return words;
    }

    public async Task<bool> MoveAsync(Guid id, string from, string to, Guid? assignTo, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.staff_tasks SET state = {to}, staff_id = COALESCE(staff_id, {assignTo})
             WHERE id = {id} AND state = {from}
            """, ct) == 1;

    public async Task<bool> AssignAsync(Guid id, Guid? staffId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE hazem.staff_tasks SET staff_id = {staffId} WHERE id = {id}", ct) == 1;

    public async Task<IReadOnlyList<StaffRef>> ActiveStaffAsync(CancellationToken ct) =>
        await db.Staff.AsNoTracking().Where(s => s.Active).OrderBy(s => s.Name).Select(s => new StaffRef(s.Id, s.Name)).ToListAsync(ct);

    public Task AuditAsync(Guid userId, string action, Guid taskId, IReadOnlyDictionary<string, string?> detail, CancellationToken ct) =>
        PanelAudit.WriteAsync(db, clock, userId, action, "task", taskId, detail, ct);
}
