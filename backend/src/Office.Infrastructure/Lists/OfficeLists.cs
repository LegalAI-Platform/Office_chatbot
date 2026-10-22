using System.Text;
using Microsoft.EntityFrameworkCore;
using Office.Application.Common;
using Office.Domain.Common;
using Office.Domain.Tasks;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Lists;

/// <summary>
/// B-14 lists from the office's files (Excel saved as «CSV UTF-8»): name, phone[, staff].
/// A bad line is refused and reported by line number with the last 3 digits only (never the
/// whole number). --replace swaps a whole list in one transaction.
/// </summary>
public sealed class ListImporter(HazemDbContext db)
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "blocked", "lawyer_direct", "existing_client" };

    public sealed record Rejected(int Line, string Reason);

    public sealed record Result(int Imported, IReadOnlyList<Rejected> Rejected);

    public async Task<Result> ImportAsync(string kind, TextReader csv, bool replace, CancellationToken ct)
    {
        if (!Kinds.Contains(kind))
        {
            throw new ArgumentException($"kind must be one of: {string.Join(", ", Kinds)}", nameof(kind));
        }

        var rows = ReadRows(csv).ToList();
        if (rows.Count == 0)
        {
            return new Result(0, [new Rejected(1, "empty file or no header (name,phone[,staff])")]);
        }

        var header = rows[0].Fields.Select(h => h.Trim().TrimStart('﻿').ToLowerInvariant()).ToList();
        int nameAt = header.IndexOf("name"), phoneAt = header.IndexOf("phone"), staffAt = header.IndexOf("staff");
        if (phoneAt < 0)
        {
            return new Result(0, [new Rejected(1, "the header must have a phone column (name,phone[,staff])")]);
        }

        var batch = Guid.NewGuid();
        var rejected = new List<Rejected>();
        var imported = 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (replace)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM hazem.list_entries WHERE list_kind = {kind}", ct);
        }

        foreach (var (line, fields) in rows.Skip(1))
        {
            string Field(int i) => i >= 0 && i < fields.Count ? fields[i].Trim() : "";
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var phone = Field(phoneAt);
            if (!PhoneNumbers.TryToWaId(phone, out var waId))
            {
                var digits = new string(phone.Where(char.IsDigit).ToArray());
                rejected.Add(new Rejected(line, $"not a phone number ({LogMask.Phone(digits)})"));
                continue;
            }

            var name = Field(nameAt);
            if (name.Length > 120)
            {
                rejected.Add(new Rejected(line, "name longer than 120 characters"));
                continue;
            }

            Guid? staffId = null;
            if (kind == "existing_client" && Field(staffAt) is { Length: > 0 } staff)
            {
                staffId = await StaffIdAsync(staff, ct);
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.list_entries (list_kind, wa_id, name, staff_id, import_batch)
                VALUES ({kind}, {waId}, {(name.Length == 0 ? null : name)}, {staffId}, {batch})
                ON CONFLICT (list_kind, wa_id) DO UPDATE SET
                  name = EXCLUDED.name, staff_id = EXCLUDED.staff_id, import_batch = EXCLUDED.import_batch, imported_at = now()
                """, ct);
            imported++;
        }

        if (replace && (rejected.Count > 0 || imported == 0))
        {
            // Swapping a whole list from a file with bad lines (or none good) would silently drop
            // people from it: nothing changes, the owner fixes the file and runs it again.
            await tx.RollbackAsync(ct);
            rejected.Add(new Rejected(0, "--replace: nothing was changed; the list stays as it was until every line is valid"));
            return new Result(0, rejected);
        }

        await tx.CommitAsync(ct);
        return new Result(imported, rejected);
    }

    private async Task<Guid> StaffIdAsync(string name, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO hazem.staff (name) VALUES ({name}) ON CONFLICT (name) DO NOTHING", ct);
        return await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM hazem.staff WHERE name = {name}").SingleAsync(ct);
    }

    /// <summary>
    /// RFC 4180-style CSV: commas, quoted fields with "" inside. A quoted cell may hold line breaks
    /// (Alt+Enter in Excel): the record goes on to the next line. Reported by its first line.
    /// </summary>
    private static IEnumerable<(int Line, List<string> Fields)> ReadRows(TextReader reader)
    {
        var lineNo = 0;
        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        var recordLine = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNo++;
            if (quoted)
            {
                sb.Append('\n'); // the line break inside the quoted cell
            }
            else
            {
                recordLine = lineNo;
            }

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else if (ch == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
                else if (ch == '"')
                {
                    quoted = true;
                }
                else if (ch is ',' or ';')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }

            if (quoted)
            {
                continue;
            }

            fields.Add(sb.ToString());
            sb.Clear();
            yield return (recordLine, fields);
            fields = [];
        }

        if (quoted)
        {
            // An unclosed quote at the end of the file: keep what was read, it is reported by its line.
            fields.Add(sb.ToString());
            yield return (recordLine, fields);
        }
    }
}

/// <summary>
/// S-05: tasks past their due time become overdue (alerts and apologies come with the panel).
/// One SQL update, with the states taken from the domain rule (<see cref="StaffTask.MayBecomeOverdue"/>).
/// </summary>
public sealed class TaskMaintenance(HazemDbContext db)
{
    private static readonly string[] From = StaffTask.MayBecomeOverdue.Select(s => StateName.Of(s)).ToArray();

    private static readonly string Overdue = StateName.Of(StaffTaskState.Overdue);

    public Task<int> MarkOverdueAsync(CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE hazem.staff_tasks SET state = {Overdue} WHERE state = ANY({From}) AND due_at < now()", ct);
}
