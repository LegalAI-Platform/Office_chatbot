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

        // Migration 0005 (TASK 04). Read through EF; written with SQL inside the turn's transaction.
        b.Entity<PersonRow>(e =>
        {
            e.ToTable("persons");
            e.Property(x => x.SourceRaw).HasColumnType("jsonb");
            e.HasKey(x => x.Id);
        });

        b.Entity<ConversationRow>(e =>
        {
            e.ToTable("conversations");
            e.HasKey(x => x.Id);
        });

        b.Entity<IntakeRow>(e =>
        {
            e.ToTable("intakes");
            e.HasKey(x => x.Id);
        });

        b.Entity<ApprovedTextRow>(e =>
        {
            e.ToTable("approved_texts");
            e.HasKey(x => new { x.Key, x.Version });
        });

        b.Entity<ListEntryRow>(e =>
        {
            e.ToTable("list_entries");
            e.HasKey(x => x.Id);
        });
    }

    public DbSet<PersonRow> Persons => Set<PersonRow>();
    public DbSet<ConversationRow> Conversations => Set<ConversationRow>();
    public DbSet<IntakeRow> Intakes => Set<IntakeRow>();
    public DbSet<ApprovedTextRow> ApprovedTexts => Set<ApprovedTextRow>();
    public DbSet<ListEntryRow> ListEntries => Set<ListEntryRow>();
}

public sealed class PersonRow
{
    public Guid Id { get; set; }
    public required string WaId { get; set; }
    public string? NameRaw { get; set; }
    public string? NameNormalized { get; set; }
    public bool? FullNameProvided { get; set; }
    public string? PreferredCallPhone { get; set; }
    public string? Source { get; set; }
    public string? SourceRaw { get; set; }
    public DateTimeOffset? FirstSeenAt { get; set; }
}

public sealed class ConversationRow
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public required string State { get; set; }
    public DateTimeOffset? LastStaffMessageAt { get; set; }
    public int AbuseWarnings { get; set; }
}

public sealed class IntakeRow
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public required string State { get; set; }
    public string? ClientType { get; set; }
    public int NameAsks { get; set; }
    public string? PendingIntent { get; set; }
    public string? PendingService { get; set; }
    public string? PendingConsultationMode { get; set; }
    public int UnclearInARow { get; set; }
    public string? SenderRole { get; set; }
    public bool ExistingQuestionAsked { get; set; }
    public string? MatterType { get; set; }
    public string? MatterSubtype { get; set; }
    public bool MatterAsked { get; set; }
}

public sealed class ListEntryRow
{
    public Guid Id { get; set; }
    public required string ListKind { get; set; }
    public required string WaId { get; set; }
    public string? Name { get; set; }
    public Guid? StaffId { get; set; }
}

public sealed class ApprovedTextRow
{
    public required string Key { get; set; }
    public int Version { get; set; }
    public required string Text { get; set; }
    public int MaxChars { get; set; }
    public bool Active { get; set; }
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
