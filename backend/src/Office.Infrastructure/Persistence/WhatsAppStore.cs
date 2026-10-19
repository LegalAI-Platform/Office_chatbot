using Microsoft.EntityFrameworkCore;
using Office.Core.WhatsApp;

namespace Office.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of the WhatsApp store. Inserts that must be idempotent use
/// INSERT ... ON CONFLICT DO NOTHING so concurrent duplicates never fail.
/// </summary>
public sealed class WhatsAppStore(OfficeDbContext db) : IWhatsAppStore
{
    public async Task InTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await work(ct);
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await work(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<Guid?> TryAddWebhookEventAsync(WebhookEvent e, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.wa_webhook_events (id, received_at, field, phone_number_id, payload, payload_sha256, signature_valid, processing_status)
            VALUES ({e.Id}, {e.ReceivedAt}, {e.Field}, {e.PhoneNumberId}, {e.Payload}::jsonb, {e.PayloadSha256}, {e.SignatureValid}, {e.ProcessingStatus})
            ON CONFLICT (payload_sha256) DO NOTHING
            """, ct);
        return rows == 1 ? e.Id : null;
    }

    public Task<WebhookEvent?> GetWebhookEventAsync(Guid id, CancellationToken ct) =>
        db.WebhookEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task MarkWebhookEventAsync(Guid id, string status, string? errorJson, DateTimeOffset at, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE app.wa_webhook_events
               SET processing_status = {status}, error = {errorJson}::jsonb, processed_at = {at}
             WHERE id = {id}
            """, ct);

    public async Task<bool> TryAddInboundMessageAsync(InboundMessage m, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.wa_inbound_messages
              (id, wamid, wa_id, contact_name, phone_number_id, message_type, sent_at, received_at, context_wamid,
               referral, is_group, raw, webhook_event_id, processing_status, updated_at)
            VALUES
              ({m.Id}, {m.Wamid}, {m.WaId}, {m.ContactName}, {m.PhoneNumberId}, {m.MessageType}, {m.SentAt}, {m.ReceivedAt}, {m.ContextWamid},
               {m.Referral}::jsonb, {m.IsGroup}, {m.Raw}::jsonb, {m.WebhookEventId}, {m.ProcessingStatus}, {m.UpdatedAt})
            ON CONFLICT (wamid) DO NOTHING
            """, ct);
        return rows == 1;
    }

    public Task OpenContactWindowAsync(string waId, DateTimeOffset at, string wamid, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.wa_contact_windows (wa_id, last_inbound_at, last_inbound_wamid, updated_at)
            VALUES ({waId}, {at}, {wamid}, now())
            ON CONFLICT (wa_id) DO UPDATE
              SET last_inbound_wamid = CASE WHEN EXCLUDED.last_inbound_at >= wa_contact_windows.last_inbound_at
                                            THEN EXCLUDED.last_inbound_wamid ELSE wa_contact_windows.last_inbound_wamid END,
                  last_inbound_at = GREATEST(wa_contact_windows.last_inbound_at, EXCLUDED.last_inbound_at),
                  updated_at = now()
            """, ct);

    public async Task<DateTimeOffset?> GetLastInboundAtAsync(string waId, CancellationToken ct) =>
        await db.ContactWindows.AsNoTracking().Where(x => x.WaId == waId)
            .Select(x => (DateTimeOffset?)x.LastInboundAt).SingleOrDefaultAsync(ct);

    public async Task<bool> TryAddMessageStatusAsync(MessageStatus s, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.wa_message_statuses (id, wamid, status, status_at, recipient_id, errors, pricing, raw, received_at)
            VALUES ({s.Id}, {s.Wamid}, {s.Status}, {s.StatusAt}, {s.RecipientId}, {s.Errors}::jsonb, {s.Pricing}::jsonb, {s.Raw}::jsonb, {s.ReceivedAt})
            ON CONFLICT (wamid, status, status_at) DO NOTHING
            """, ct);
        return rows == 1;
    }

    public async Task AdvanceOutboundStatusAsync(string wamid, string status, string? errorsJson, CancellationToken ct)
    {
        // Lock the row so two status events for the same message cannot race.
        var current = await db.OutboundMessages
            .FromSqlInterpolated($"SELECT * FROM app.wa_outbound_messages WHERE wamid = {wamid} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (current is null || !OutboundStatuses.CanAdvance(current.Status, status))
        {
            return;
        }

        current.Status = status;
        if (status == OutboundStatuses.Failed)
        {
            current.LastError = errorsJson;
        }

        current.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        db.Entry(current).State = EntityState.Detached;
    }

    public Task UpsertTemplateStatusAsync(string name, string language, string? status, string? metaTemplateId, string? reason, DateTimeOffset at, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.wa_templates (name, language, status, meta_template_id, rejection_reason, last_synced_at, updated_at)
            VALUES ({name}, {language}, {status}, {metaTemplateId}, {reason}, {at}, now())
            ON CONFLICT (name, language) DO UPDATE
              SET status = EXCLUDED.status,
                  meta_template_id = COALESCE(EXCLUDED.meta_template_id, wa_templates.meta_template_id),
                  rejection_reason = EXCLUDED.rejection_reason,
                  last_synced_at = EXCLUDED.last_synced_at,
                  updated_at = now()
            """, ct);

    public Task UpsertTemplateAsync(WhatsAppTemplate t, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.wa_templates (name, language, category, status, meta_template_id, components, rejection_reason, last_synced_at, updated_at)
            VALUES ({t.Name}, {t.Language}, {t.Category}, {t.Status}, {t.MetaTemplateId}, {t.Components}::jsonb, {t.RejectionReason}, {t.LastSyncedAt}, now())
            ON CONFLICT (name, language) DO UPDATE
              SET category = EXCLUDED.category, status = EXCLUDED.status, meta_template_id = EXCLUDED.meta_template_id,
                  components = EXCLUDED.components, rejection_reason = EXCLUDED.rejection_reason,
                  last_synced_at = EXCLUDED.last_synced_at, updated_at = now()
            """, ct);

    public Task<bool> IsTemplateApprovedAsync(string name, string language, CancellationToken ct) =>
        db.Templates.AsNoTracking().AnyAsync(x => x.Name == name && x.Language == language && x.Status == "APPROVED", ct);

    public async Task<bool> TryAddOutboundAsync(OutboundMessage o, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.wa_outbound_messages
              (id, idempotency_key, wa_id, kind, template_name, template_language, request_payload, status, attempts, requested_by, created_at, updated_at)
            VALUES
              ({o.Id}, {o.IdempotencyKey}, {o.WaId}, {o.Kind}, {o.TemplateName}, {o.TemplateLanguage}, {o.RequestPayload}::jsonb,
               {o.Status}, {o.Attempts}, {o.RequestedBy}, {o.CreatedAt}, {o.UpdatedAt})
            ON CONFLICT (idempotency_key) DO NOTHING
            """, ct);
        return rows == 1;
    }

    public Task<OutboundMessage?> GetOutboundByKeyAsync(string idempotencyKey, CancellationToken ct) =>
        db.OutboundMessages.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);

    public Task SaveOutboundAsync(OutboundMessage o, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE app.wa_outbound_messages
               SET status = {o.Status}, attempts = {o.Attempts}, wamid = {o.Wamid}, response_payload = {o.ResponsePayload}::jsonb,
                   last_error = {o.LastError}::jsonb, sent_at = {o.SentAt}, updated_at = now()
             WHERE id = {o.Id}
            """, ct);

    public async Task<IReadOnlyList<InboundMessage>> GetPendingTextMessagesAsync(IReadOnlyCollection<string> fromWaIds, int limit, CancellationToken ct) =>
        await db.InboundMessages.AsNoTracking()
            .Where(x => x.ProcessingStatus == InboundStatuses.Pending && x.MessageType == "text" && fromWaIds.Contains(x.WaId))
            .OrderBy(x => x.ReceivedAt)
            .Take(limit)
            .ToListAsync(ct);
}
