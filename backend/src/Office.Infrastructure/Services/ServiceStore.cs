using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Services;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Services;

/// <summary>hazem.service_orders and hazem.service_files (migration 0015), through its SQL functions.</summary>
public sealed class ServiceStore(HazemDbContext db) : IServiceStore
{
    public async Task<FileFor> FileTargetAsync(Guid personId, bool image, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<FileTargetRow>($"""
            SELECT t.kind, t.id FROM hazem.file_target({personId}, {image}) t
            """).FirstOrDefaultAsync(ct);
        return row is null
            ? FileFor.Nothing
            : new FileFor(row.Kind switch
            {
                "hold" => FileTarget.Hold,
                "order_payment" => FileTarget.OrderPayment,
                "booking_payment" => FileTarget.BookingPayment,
                "order_documents" => FileTarget.OrderDocuments,
                "late_hold" => FileTarget.LateHold,
                "ask_image" => FileTarget.AskImage,
                _ => FileTarget.None,
            }, row.Id);
    }

    public async Task<OpenOrderOutcome> OpenAsync(Guid personId, Guid? intakeId, ServiceKind kind, string key, CancellationToken ct)
    {
        using var doc = await JsonAsync($"SELECT hazem.open_service_order({personId}, {intakeId}, {kind.Code}, {key})::text AS \"Value\"", ct);
        var r = doc.RootElement;
        return new OpenOrderOutcome(r.GetProperty("status").GetString() switch
        {
            "opened" => OpenOrderStatus.Opened,
            "open" => OpenOrderStatus.Open,
            _ => OpenOrderStatus.Exists,
        }, r.GetProperty("id").GetGuid(), r.GetProperty("state").GetString()!);
    }

    public async Task<AddFileOutcome> AddFileAsync(Guid personId, StoredFile f, CancellationToken ct)
    {
        using var doc = await JsonAsync($"""
            SELECT hazem.add_service_file({personId}, {f.Wamid}, {f.Bucket}, {f.Key}, {f.MimeType}, {f.Filename})::text AS "Value"
            """, ct);
        var r = doc.RootElement;
        return new AddFileOutcome(r.GetProperty("status").GetString() switch
        {
            "first" => AddFileStatus.First,
            "more" => AddFileStatus.More,
            "exists" => AddFileStatus.Exists,
            _ => AddFileStatus.None,
        }, r.TryGetProperty("id", out var id) ? id.GetGuid() : null, r.TryGetProperty("kind", out var k) ? k.GetString() : null);
    }

    private async Task<JsonDocument> JsonAsync(FormattableString sql, CancellationToken ct) =>
        JsonDocument.Parse(await db.Database.SqlQuery<string>(sql).SingleAsync(ct));

    private sealed record FileTargetRow(string Kind, Guid Id);
}
