using Microsoft.EntityFrameworkCore;

namespace Office.Infrastructure.Persistence;

/// <summary>
/// Schema "hazem" (migration 0003), used as office_api. The schema is created and changed by
/// dbmate only, never by EF. The API has no access to schema "app" (the WhatsApp tables).
/// </summary>
public sealed class HazemDbContext(DbContextOptions<HazemDbContext> options) : DbContext(options)
{
    public const string Schema = "hazem";

    public DbSet<SettingRow> Settings => Set<SettingRow>();
    public DbSet<TurnRow> Turns => Set<TurnRow>();
    public DbSet<SendResultRow> SendResults => Set<SendResultRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<SettingRow>(e =>
        {
            e.ToTable("settings");
            e.HasKey(x => x.Key);
        });

        b.Entity<TurnRow>(e =>
        {
            e.ToTable("turns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Messages).HasColumnType("jsonb");
            e.Property(x => x.Actions).HasColumnType("jsonb");
        });

        b.Entity<SendResultRow>(e =>
        {
            e.ToTable("send_results");
            e.HasKey(x => x.IdempotencyKey);
        });
    }
}

public sealed class SettingRow
{
    public required string Key { get; set; }
    public string Value { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class TurnRow
{
    public Guid Id { get; set; }
    public required string TurnId { get; set; }
    public required string WaId { get; set; }
    public string? ContactName { get; set; }
    public required string Messages { get; set; }
    public required string Status { get; set; }
    public required string Responder { get; set; }
    public required string Actions { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SendResultRow
{
    public required string IdempotencyKey { get; set; }
    public string? TurnId { get; set; }
    public bool Ok { get; set; }
    public string? OutboundId { get; set; }
    public string? Wamid { get; set; }
    public string? Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public required string ReportedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
