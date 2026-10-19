using Microsoft.EntityFrameworkCore;
using Office.Core.WhatsApp;

namespace Office.Infrastructure.Persistence;

public sealed class OfficeDbContext(DbContextOptions<OfficeDbContext> options) : DbContext(options)
{
    public const string Schema = "app";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<InboundMessage> InboundMessages => Set<InboundMessage>();
    public DbSet<ContactWindow> ContactWindows => Set<ContactWindow>();
    public DbSet<OutboundMessage> OutboundMessages => Set<OutboundMessage>();
    public DbSet<MessageStatus> MessageStatuses => Set<MessageStatus>();
    public DbSet<WhatsAppTemplate> Templates => Set<WhatsAppTemplate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<WebhookEvent>(e =>
        {
            e.ToTable("wa_webhook_events", t => t.HasCheckConstraint("ck_wa_webhook_events_status", InList("processing_status", WebhookEventStatuses.All)));
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.Error).HasColumnType("jsonb");
            e.Property(x => x.ProcessingStatus).HasMaxLength(32);
            e.HasIndex(x => x.PayloadSha256).IsUnique();
            e.HasIndex(x => new { x.ProcessingStatus, x.ReceivedAt });
        });

        b.Entity<InboundMessage>(e =>
        {
            e.ToTable("wa_inbound_messages", t => t.HasCheckConstraint("ck_wa_inbound_messages_status", InList("processing_status", InboundStatuses.All)));
            e.HasKey(x => x.Id);
            e.Property(x => x.Raw).HasColumnType("jsonb");
            e.Property(x => x.Referral).HasColumnType("jsonb");
            e.Property(x => x.ProcessingStatus).HasMaxLength(32);
            e.HasIndex(x => x.Wamid).IsUnique();
            e.HasIndex(x => new { x.WaId, x.SentAt });
            e.HasIndex(x => new { x.ProcessingStatus, x.ReceivedAt });
            e.HasOne<WebhookEvent>().WithMany().HasForeignKey(x => x.WebhookEventId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ContactWindow>(e =>
        {
            e.ToTable("wa_contact_windows");
            e.HasKey(x => x.WaId);
        });

        b.Entity<OutboundMessage>(e =>
        {
            e.ToTable("wa_outbound_messages", t =>
            {
                t.HasCheckConstraint("ck_wa_outbound_messages_status", InList("status", OutboundStatuses.All));
                t.HasCheckConstraint("ck_wa_outbound_messages_kind", InList("kind", OutboundKinds.All));
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.RequestPayload).HasColumnType("jsonb");
            e.Property(x => x.ResponsePayload).HasColumnType("jsonb");
            e.Property(x => x.LastError).HasColumnType("jsonb");
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.Wamid).IsUnique();
            e.HasIndex(x => new { x.WaId, x.CreatedAt });
        });

        b.Entity<MessageStatus>(e =>
        {
            e.ToTable("wa_message_statuses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Errors).HasColumnType("jsonb");
            e.Property(x => x.Pricing).HasColumnType("jsonb");
            e.Property(x => x.Raw).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Wamid, x.Status, x.StatusAt }).IsUnique();
        });

        b.Entity<WhatsAppTemplate>(e =>
        {
            e.ToTable("wa_templates");
            e.HasKey(x => new { x.Name, x.Language });
            e.Property(x => x.Components).HasColumnType("jsonb");
        });
    }

    private static string InList(string column, IEnumerable<string> values) =>
        $"{column} IN ({string.Join(", ", values.Select(v => $"'{v}'"))})";
}
