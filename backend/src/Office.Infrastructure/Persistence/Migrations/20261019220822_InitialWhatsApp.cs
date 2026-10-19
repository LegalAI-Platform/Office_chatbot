using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Foundations formerly in database/migrations/0001_base.sql (dbmate retired, T-19).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext;");

            migrationBuilder.EnsureSchema(
                name: "app");

            // office_app: data only (the API). office_readonly: reports. Applies to every
            // table/sequence office_migrator creates from now on, in this schema.
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA app TO office_app, office_readonly;
                ALTER DEFAULT PRIVILEGES IN SCHEMA app GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO office_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA app GRANT USAGE, SELECT ON SEQUENCES TO office_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA app GRANT SELECT ON TABLES TO office_readonly;
                GRANT SELECT ON ALL TABLES IN SCHEMA app TO office_readonly;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA app TO office_app;
                CREATE OR REPLACE FUNCTION app.set_updated_at() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  NEW.updated_at := now();
                  RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.CreateTable(
                name: "wa_contact_windows",
                schema: "app",
                columns: table => new
                {
                    wa_id = table.Column<string>(type: "text", nullable: false),
                    last_inbound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_inbound_wamid = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wa_contact_windows", x => x.wa_id);
                });

            migrationBuilder.CreateTable(
                name: "wa_message_statuses",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wamid = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    status_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recipient_id = table.Column<string>(type: "text", nullable: true),
                    errors = table.Column<string>(type: "jsonb", nullable: true),
                    pricing = table.Column<string>(type: "jsonb", nullable: true),
                    raw = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wa_message_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wa_outbound_messages",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    wa_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    template_name = table.Column<string>(type: "text", nullable: true),
                    template_language = table.Column<string>(type: "text", nullable: true),
                    request_payload = table.Column<string>(type: "jsonb", nullable: false),
                    response_payload = table.Column<string>(type: "jsonb", nullable: true),
                    wamid = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "jsonb", nullable: true),
                    requested_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wa_outbound_messages", x => x.id);
                    table.CheckConstraint("ck_wa_outbound_messages_kind", "kind IN ('text', 'template', 'media')");
                    table.CheckConstraint("ck_wa_outbound_messages_status", "status IN ('queued', 'sending', 'sent', 'delivered', 'read', 'failed', 'blocked_window')");
                });

            migrationBuilder.CreateTable(
                name: "wa_templates",
                schema: "app",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    meta_template_id = table.Column<string>(type: "text", nullable: true),
                    components = table.Column<string>(type: "jsonb", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wa_templates", x => new { x.name, x.language });
                });

            migrationBuilder.CreateTable(
                name: "wa_webhook_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    field = table.Column<string>(type: "text", nullable: true),
                    phone_number_id = table.Column<string>(type: "text", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    payload_sha256 = table.Column<string>(type: "text", nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    processing_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error = table.Column<string>(type: "jsonb", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wa_webhook_events", x => x.id);
                    table.CheckConstraint("ck_wa_webhook_events_status", "processing_status IN ('received', 'processed', 'ignored', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "wa_inbound_messages",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wamid = table.Column<string>(type: "text", nullable: false),
                    wa_id = table.Column<string>(type: "text", nullable: false),
                    contact_name = table.Column<string>(type: "text", nullable: true),
                    phone_number_id = table.Column<string>(type: "text", nullable: false),
                    message_type = table.Column<string>(type: "text", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    context_wamid = table.Column<string>(type: "text", nullable: true),
                    referral = table.Column<string>(type: "jsonb", nullable: true),
                    is_group = table.Column<bool>(type: "boolean", nullable: false),
                    raw = table.Column<string>(type: "jsonb", nullable: false),
                    webhook_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processing_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wa_inbound_messages", x => x.id);
                    table.CheckConstraint("ck_wa_inbound_messages_status", "processing_status IN ('pending', 'processing', 'processed', 'ignored_group', 'ignored_unsupported', 'failed')");
                    table.ForeignKey(
                        name: "fk_wa_inbound_messages_wa_webhook_events_webhook_event_id",
                        column: x => x.webhook_event_id,
                        principalSchema: "app",
                        principalTable: "wa_webhook_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wa_inbound_messages_processing_status_received_at",
                schema: "app",
                table: "wa_inbound_messages",
                columns: new[] { "processing_status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wa_inbound_messages_wa_id_sent_at",
                schema: "app",
                table: "wa_inbound_messages",
                columns: new[] { "wa_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wa_inbound_messages_wamid",
                schema: "app",
                table: "wa_inbound_messages",
                column: "wamid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wa_inbound_messages_webhook_event_id",
                schema: "app",
                table: "wa_inbound_messages",
                column: "webhook_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_wa_message_statuses_wamid_status_status_at",
                schema: "app",
                table: "wa_message_statuses",
                columns: new[] { "wamid", "status", "status_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wa_outbound_messages_idempotency_key",
                schema: "app",
                table: "wa_outbound_messages",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wa_outbound_messages_wa_id_created_at",
                schema: "app",
                table: "wa_outbound_messages",
                columns: new[] { "wa_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wa_outbound_messages_wamid",
                schema: "app",
                table: "wa_outbound_messages",
                column: "wamid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wa_webhook_events_payload_sha256",
                schema: "app",
                table: "wa_webhook_events",
                column: "payload_sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wa_webhook_events_processing_status_received_at",
                schema: "app",
                table: "wa_webhook_events",
                columns: new[] { "processing_status", "received_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wa_contact_windows",
                schema: "app");

            migrationBuilder.DropTable(
                name: "wa_inbound_messages",
                schema: "app");

            migrationBuilder.DropTable(
                name: "wa_message_statuses",
                schema: "app");

            migrationBuilder.DropTable(
                name: "wa_outbound_messages",
                schema: "app");

            migrationBuilder.DropTable(
                name: "wa_templates",
                schema: "app");

            migrationBuilder.DropTable(
                name: "wa_webhook_events",
                schema: "app");
        }
    }
}
