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
        b.Entity<AppointmentReminderRow>(e =>
        {
            e.ToTable("appointment_reminders");
            e.HasKey(x => x.Id);
        });
        b.Entity<PaymentRow>(e =>
        {
            e.ToTable("payments");
            e.HasKey(x => x.Id);
        });
        b.Entity<PaymentReceiptRow>(e =>
        {
            e.ToTable("payment_receipts");
            e.HasKey(x => x.Id);
        });
        b.Entity<ServiceOrderRow>(e =>
        {
            e.ToTable("service_orders");
            e.HasKey(x => x.Id);
        });
        b.Entity<ServiceFileRow>(e =>
        {
            e.ToTable("service_files");
            e.HasKey(x => x.Id);
        });
        b.Entity<UrfCaseRow>(e =>
        {
            e.ToTable("urf_cases");
            e.HasKey(x => x.Id);
        });
        b.Entity<UrfFileRow>(e =>
        {
            e.ToTable("urf_files");
            e.HasKey(x => x.Id);
        });
        b.Entity<ServiceRatingRow>(e =>
        {
            e.ToTable("service_ratings");
            e.HasKey(x => x.Id);
        });
    }

    public DbSet<BookingWeeklyHourRow> BookingWeeklyHours => Set<BookingWeeklyHourRow>();
    public DbSet<BookingExceptionRow> BookingExceptions => Set<BookingExceptionRow>();
    public DbSet<BookingChangeRow> BookingChanges => Set<BookingChangeRow>();
    public DbSet<AppointmentRow> Appointments => Set<AppointmentRow>();
    public DbSet<AppointmentReminderRow> AppointmentReminders => Set<AppointmentReminderRow>();
    public DbSet<PaymentRow> Payments => Set<PaymentRow>();
    public DbSet<PaymentReceiptRow> PaymentReceipts => Set<PaymentReceiptRow>();
    public DbSet<ServiceOrderRow> ServiceOrders => Set<ServiceOrderRow>();
    public DbSet<ServiceFileRow> ServiceFiles => Set<ServiceFileRow>();
    public DbSet<UrfCaseRow> UrfCases => Set<UrfCaseRow>();
    public DbSet<UrfFileRow> UrfFiles => Set<UrfFileRow>();
    public DbSet<ServiceRatingRow> ServiceRatings => Set<ServiceRatingRow>(); // 0033 (v1.31, B-26)

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
    public Guid? TaskId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EmailedAt { get; set; }
    public string? SystemWhat { get; set; }  // TASK 13: a system alert (no task)
    public string? SystemRef { get; set; }
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
    public string? ClientNotice { get; set; }
    public DateTimeOffset? HoldUntil { get; set; }
    public DateTimeOffset? HoldRemindedAt { get; set; }
    public string? NoticeKind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AttendanceAt { get; set; }   // TASK 12: «هحضر ✅»
    public DateTimeOffset? AttendanceFor { get; set; }  // for this time (a moved appointment is not confirmed again)
    public string? CallApp { get; set; }                // 1.25 (B-23): whatsapp, facetime or meet (a call only)
    public DateTimeOffset? CompletedAt { get; set; }    // 1.31 (B-26): when the office marked it completed
    public DateTimeOffset? UrgentAlertedAt { get; set; }   // 1.33 (B-09): the template urgent_alert went to the lawyer (or could not)
    public Guid? UrgentAlertReceipt { get; set; }          // the receipt that alert was for (a new receipt alerts again)
    public DateTimeOffset? UrgentAckedAt { get; set; }     // «التحويل تمام، هكلمه» pressed
    public DateTimeOffset? UrgentEscalatedAt { get; set; } // that alert not decided in time: the manager
    public DateTimeOffset? UrgentWaitingAt { get; set; }   // urgent_waiting told the client (once per call)
}

/// <summary>hazem.service_ratings (0033, v1.31, B-26).</summary>
public sealed class ServiceRatingRow
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? ServiceOrderId { get; set; }
    public required string Service { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public required string State { get; set; }
    public string? SkipReason { get; set; }
    public string? Channel { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public short? Score { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public string? AnsweredTurnId { get; set; }
    public DateTimeOffset? ReasonAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>hazem.appointment_reminders (0017, TASK 12, B-18).</summary>
public sealed class AppointmentReminderRow
{
    public long Id { get; set; }
    public Guid AppointmentId { get; set; }
    public required string Kind { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public required string State { get; set; }
    public string? SkipReason { get; set; }
    public string? Channel { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>hazem.payments (0014, S-04).</summary>
public sealed class PaymentRow
{
    public Guid Id { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? ServiceOrderId { get; set; }
    public required string State { get; set; }
    public int AmountDue { get; set; }
    public int? AmountReceived { get; set; }
    public DateTimeOffset? ProofAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public int? RefundDue { get; set; }
    public string? RefundReason { get; set; }
    public Guid? RefundedBy { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundMethod { get; set; }
    public string? RefundNote { get; set; }
    public string? ClientNotice { get; set; }
    public string? NoticeKind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>hazem.service_orders (0015, S-07).</summary>
public sealed class ServiceOrderRow
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public Guid? IntakeId { get; set; }
    public required string Kind { get; set; }
    public required string State { get; set; }
    public Guid? AppointmentId { get; set; }
    public required string OpenedKey { get; set; }
    public int? Price { get; set; }
    public bool PriceException { get; set; }
    public DateOnly? DueOn { get; set; }
    public string? Delivery { get; set; }
    public Guid? PricedBy { get; set; }
    public DateTimeOffset? PricedAt { get; set; }
    public DateTimeOffset? PriceUntil { get; set; }
    public DateTimeOffset? PriceRemindedAt { get; set; }
    public string? FileBucket { get; set; }
    public string? FileKey { get; set; }
    public string? FileName { get; set; }
    public string? FileMime { get; set; }
    public Guid? DeliveredBy { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? ClosedReason { get; set; }
    public string? ClientNotice { get; set; }
    public string? NoticeKind { get; set; }
    public int NoticeAttempt { get; set; }
    public DateTimeOffset? PriceExpiredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>hazem.service_files (0015): where a client's document for an order is; never its content.</summary>
public sealed class ServiceFileRow
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public required string Wamid { get; set; }
    public required string Bucket { get; set; }
    public required string Key { get; set; }
    public string? Mime { get; set; }
    public string? Filename { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>hazem.payment_receipts (0014): where a receipt image is; never its content.</summary>
public sealed class PaymentReceiptRow
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public required string Wamid { get; set; }
    public required string Bucket { get; set; }
    public required string Key { get; set; }
    public required string Sha256 { get; set; }
    public string? Mime { get; set; }
    public Guid? DuplicateOf { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? UrgentDecision { get; set; }          // 1.33 (B-09): ok / bad, the lawyer's (or the manager's) press on it
    public DateTimeOffset? UrgentDecidedAt { get; set; }
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

/// <summary>hazem.urf_cases (0016): an Urfi case (S-06), its answers and the lawyer's decision.</summary>
public sealed class UrfCaseRow
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public Guid? IntakeId { get; set; }
    public required string Kind { get; set; }
    public required string State { get; set; }
    public string? Step { get; set; }
    public int? HusbandAge { get; set; }
    public int? WifeAge { get; set; }
    public string? WifePast { get; set; }
    public string? Guardian { get; set; }
    public string? Witnesses { get; set; }
    public bool? HasCopy { get; set; }
    public bool? Agreed { get; set; }
    public bool? Children { get; set; }
    public int AgeTries { get; set; }
    public Guid? AppointmentId { get; set; }
    public string? PolicyVersion { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? ClientNotice { get; set; }
    public string? NoticeKind { get; set; }
    public DateTimeOffset? DocsTaskAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>hazem.urf_files (0016): where a document of an Urfi case is; never its content.</summary>
public sealed class UrfFileRow
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public required string Wamid { get; set; }
    public required string Bucket { get; set; }
    public required string Key { get; set; }
    public string? Mime { get; set; }
    public string? Filename { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
