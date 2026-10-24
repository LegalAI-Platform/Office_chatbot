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

        // Migration 0008 (TASK 05a), read by the panel.
        b.Entity<StaffRow>(e =>
        {
            e.ToTable("staff");
            e.HasKey(x => x.Id);
        });

        b.Entity<StaffTaskRow>(e =>
        {
            e.ToTable("staff_tasks");
            e.HasKey(x => x.Id);
        });

        // Migration 0009 (TASK 07a): panel accounts. Written with SQL (see PanelUserStore).
        b.Entity<PanelUserRow>(e =>
        {
            e.ToTable("panel_users");
            e.HasKey(x => x.Id);
        });
        // Migration 0010: e-mail codes and recovery codes (read here, written with SQL).
        b.Entity<PanelCodeRow>(e =>
        {
            e.ToTable("panel_codes");
            e.HasKey(x => x.Id);
        });
        b.Entity<PanelRecoveryCodeRow>(e =>
        {
            e.ToTable("panel_recovery_codes");
            e.HasKey(x => x.Id);
        });
        // Migration 0011 (TASK 07b): the link to Chatwoot.
        b.Entity<ChatwootThreadRow>(e =>
        {
            e.ToTable("chatwoot_threads");
            e.HasKey(x => x.WaId);
        });
        b.Entity<ChatwootTurnRow>(e =>
        {
            e.ToTable("chatwoot_turns");
            e.HasKey(x => x.TurnId);
        });
        // Migration 0012 (TASK 07c): alerts (read here, written with SQL).
        b.Entity<PanelAuditRow>(e =>
        {
            e.ToTable("panel_audit");
            e.HasKey(x => x.Id);
        });
        b.Entity<PanelAlertRow>(e =>
        {
            e.ToTable("panel_alerts");
            e.HasKey(x => x.Id);
        });
        b.Entity<PanelAlertReadRow>(e =>
        {
            e.ToTable("panel_alert_reads");
            e.HasKey(x => new { x.AlertId, x.UserId });
        });
        // Migration 0013 (TASK 08): the booking. Read here; appointments are written by the SQL functions.
        b.Entity<BookingWeeklyHourRow>(e =>
        {
            e.ToTable("booking_weekly_hours");
            e.HasKey(x => new { x.Weekday, x.StartMinute });
        });
        b.Entity<BookingExceptionRow>(e =>
        {
            e.ToTable("booking_exceptions");
            e.HasKey(x => x.Id);
        });
        b.Entity<BookingChangeRow>(e =>
        {
            e.ToTable("booking_changes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Before).HasColumnType("jsonb");
            e.Property(x => x.After).HasColumnType("jsonb");
        });
        b.Entity<AppointmentRow>(e =>
        {
            e.ToTable("appointments");
            e.HasKey(x => x.Id);
        });
    }

    public DbSet<BookingWeeklyHourRow> BookingWeeklyHours => Set<BookingWeeklyHourRow>();
    public DbSet<BookingExceptionRow> BookingExceptions => Set<BookingExceptionRow>();
    public DbSet<BookingChangeRow> BookingChanges => Set<BookingChangeRow>();
    public DbSet<AppointmentRow> Appointments => Set<AppointmentRow>();

    public DbSet<StaffRow> Staff => Set<StaffRow>();
    public DbSet<StaffTaskRow> StaffTasks => Set<StaffTaskRow>();
    public DbSet<PanelUserRow> PanelUsers => Set<PanelUserRow>();
    public DbSet<PanelCodeRow> PanelCodes => Set<PanelCodeRow>();
    public DbSet<PanelRecoveryCodeRow> PanelRecoveryCodes => Set<PanelRecoveryCodeRow>();
    public DbSet<ChatwootThreadRow> ChatwootThreads => Set<ChatwootThreadRow>();
    public DbSet<ChatwootTurnRow> ChatwootTurns => Set<ChatwootTurnRow>();
    public DbSet<PanelAlertRow> PanelAlerts => Set<PanelAlertRow>();
    public DbSet<PanelAuditRow> PanelAuditRows => Set<PanelAuditRow>();
    public DbSet<PanelAlertReadRow> PanelAlertReads => Set<PanelAlertReadRow>();

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
    public DateTimeOffset UpdatedAt { get; set; } // read only (written with SQL; the trigger keeps it)
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

public sealed class StaffRow
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; }
}

public sealed class StaffTaskRow
{
    public Guid Id { get; set; }
    public required string Type { get; set; }
    public required string State { get; set; }
    public Guid? StaffId { get; set; }
    public Guid PersonId { get; set; }
    public Guid? IntakeId { get; set; }
    public required string TurnId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PanelUserRow
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public Guid? StaffId { get; set; }
    public required string PasswordHash { get; set; }
    public required string SecurityStamp { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public long? ChatwootUserId { get; set; }
    public required string Approval { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; }
    public long? TotpLastStep { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PanelCodeRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Purpose { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ChatwootThreadRow
{
    public required string WaId { get; set; }
    public long ContactId { get; set; }
    public required string SourceId { get; set; }
    public long ConversationId { get; set; }
}

public sealed class PanelAlertRow
{
    public long Id { get; set; }
    public required string Kind { get; set; }
    public Guid TaskId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EmailedAt { get; set; }
}

public sealed class PanelAlertReadRow
{
    public long AlertId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ReadAt { get; set; }
}

public sealed class ChatwootTurnRow
{
    public required string TurnId { get; set; }
    public required string State { get; set; }
    public int Attempts { get; set; }
    public int Posted { get; set; }
    public DateTimeOffset? NextAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class PanelRecoveryCodeRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ListEntryRow
{
    public Guid Id { get; set; }
    public required string ListKind { get; set; }
    public required string WaId { get; set; }
    public string? Name { get; set; }
    public Guid? StaffId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}

public sealed class ApprovedTextRow
{
    public required string Key { get; set; }
    public int Version { get; set; }
    public required string Text { get; set; }
    public int MaxChars { get; set; }
    public bool Active { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PanelAuditRow
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
}

public sealed class BookingWeeklyHourRow
{
    public short Weekday { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}

public sealed class BookingExceptionRow
{
    public Guid Id { get; set; }
    public DateOnly Day { get; set; }
    public required string Kind { get; set; }
    public int? StartMinute { get; set; }
    public int? EndMinute { get; set; }
    public string? Reason { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BookingChangeRow
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? UserId { get; set; }
    public required string What { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }
}

public sealed class AppointmentRow
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public Guid? IntakeId { get; set; }
    public required string Service { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public required string State { get; set; }
    public int Amount { get; set; }
    public int Reschedules { get; set; }
    public string? MatterType { get; set; }
    public required string BookedTurnId { get; set; }
    public string? LastMoveTurnId { get; set; }
    public string? CalendarEventId { get; set; }
    public required string CalendarState { get; set; }
    public int CalendarAttempts { get; set; }
    public DateTimeOffset CalendarNextAt { get; set; }
    public DateTimeOffset? ConflictNotedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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
