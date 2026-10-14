using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class NativePostgresEnums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_sessions_connector_claim",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ux_sessions_reservation_claim",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ix_reservations_ends_at_id",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservations_cancelled_at",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservations_expired_at",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservation_notifications_kind",
                schema: "charging",
                table: "reservation_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_delivery_attempts_capture",
                schema: "charging",
                table: "notification_delivery_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_delivery_attempts_evidence",
                schema: "charging",
                table: "notification_delivery_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_delivery_attempts_outcome",
                schema: "charging",
                table: "notification_delivery_attempts");

            migrationBuilder.Sql("""
                ALTER TABLE charging.reservations DROP CONSTRAINT ex_reservations_connector_slot;
                ALTER TABLE charging.reservations DROP CONSTRAINT ex_reservations_driver_slot;
                """);

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .Annotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .Annotation("Npgsql:Enum:charging.file_kind", "diagnostic_bundle,session_summary,station_original,station_variant")
                .Annotation("Npgsql:Enum:charging.file_state", "awaiting_upload,processed,published,uploaded")
                .Annotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .Annotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,created,expired")
                .Annotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .Annotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping");

            // Integer-to-enum conversion requires an explicit USING expression.
            migrationBuilder.Sql("""
                ALTER TABLE charging.sessions ALTER COLUMN lifecycle
                    TYPE charging.session_lifecycle USING (CASE lifecycle
                        WHEN 0 THEN 'starting'
                        WHEN 1 THEN 'active'
                        WHEN 2 THEN 'stopping'
                        WHEN 3 THEN 'completed'
                        WHEN 4 THEN 'failed'
                        ELSE lifecycle::text END)::charging.session_lifecycle;
                ALTER TABLE charging.reservations ALTER COLUMN state
                    TYPE charging.reservation_state USING (CASE state
                        WHEN 0 THEN 'active'
                        WHEN 1 THEN 'consumed'
                        WHEN 2 THEN 'cancelled'
                        WHEN 3 THEN 'expired'
                        ELSE state::text END)::charging.reservation_state;
                ALTER TABLE charging.reservation_notifications ALTER COLUMN kind
                    TYPE charging.reservation_notification_kind USING (CASE kind
                        WHEN 0 THEN 'created'
                        WHEN 1 THEN 'cancelled'
                        WHEN 2 THEN 'expired'
                        ELSE kind::text END)::charging.reservation_notification_kind;
                ALTER TABLE charging.operations ALTER COLUMN outcome
                    TYPE charging.command_outcome USING (CASE outcome
                        WHEN 0 THEN 'pending'
                        WHEN 1 THEN 'confirmed'
                        WHEN 2 THEN 'rejected'
                        WHEN 3 THEN 'uncertain'
                        ELSE outcome::text END)::charging.command_outcome;
                ALTER TABLE charging.operations ALTER COLUMN kind
                    TYPE charging.charging_command_kind USING (CASE kind
                        WHEN 0 THEN 'start'
                        WHEN 1 THEN 'stop'
                        ELSE kind::text END)::charging.charging_command_kind;
                ALTER TABLE charging.notification_delivery_attempts ALTER COLUMN outcome
                    TYPE charging.notification_delivery_outcome USING (CASE outcome
                        WHEN 1 THEN 'uncertain'
                        WHEN 2 THEN 'captured'
                        WHEN 3 THEN 'failed'
                        WHEN 4 THEN 'not_captured'
                        ELSE outcome::text END)::charging.notification_delivery_outcome;
                ALTER TABLE charging.file_metadata ALTER COLUMN state
                    TYPE charging.file_state USING (CASE state
                        WHEN 0 THEN 'awaiting_upload'
                        WHEN 1 THEN 'uploaded'
                        WHEN 2 THEN 'processed'
                        WHEN 3 THEN 'published'
                        ELSE state::text END)::charging.file_state;
                ALTER TABLE charging.file_metadata ALTER COLUMN kind
                    TYPE charging.file_kind USING (CASE kind
                        WHEN 0 THEN 'station_original'
                        WHEN 1 THEN 'station_variant'
                        WHEN 2 THEN 'session_summary'
                        WHEN 3 THEN 'diagnostic_bundle'
                        ELSE kind::text END)::charging.file_kind;
                ALTER TABLE charging.attempts ALTER COLUMN outcome
                    TYPE charging.command_outcome USING (CASE outcome
                        WHEN 0 THEN 'pending'
                        WHEN 1 THEN 'confirmed'
                        WHEN 2 THEN 'rejected'
                        WHEN 3 THEN 'uncertain'
                        ELSE outcome::text END)::charging.command_outcome;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_sessions_connector_claim",
                schema: "charging",
                table: "sessions",
                column: "connector_id",
                unique: true,
                filter: "lifecycle IN ('starting', 'active', 'stopping')");

            migrationBuilder.CreateIndex(
                name: "ux_sessions_reservation_claim",
                schema: "charging",
                table: "sessions",
                column: "reservation_id",
                unique: true,
                filter: "lifecycle <> 'failed'");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_ends_at_id",
                schema: "charging",
                table: "reservations",
                columns: new[] { "ends_at", "id" },
                filter: "state = 'active'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservations_cancelled_at",
                schema: "charging",
                table: "reservations",
                sql: "cancelled_at IS NULL OR (state = 'cancelled' AND isfinite(cancelled_at))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservations_expired_at",
                schema: "charging",
                table: "reservations",
                sql: "expired_at IS NULL OR (state = 'expired' AND isfinite(expired_at))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_delivery_attempts_capture",
                schema: "charging",
                table: "notification_delivery_attempts",
                sql: "(outcome = 'captured') = (external_message_id IS NOT NULL) AND (external_message_id IS NULL OR length(external_message_id) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_delivery_attempts_evidence",
                schema: "charging",
                table: "notification_delivery_attempts",
                sql: "outcome = 'uncertain' OR completed_at IS NOT NULL");

            migrationBuilder.Sql("""
                ALTER TABLE charging.reservations
                ADD CONSTRAINT ex_reservations_connector_slot
                EXCLUDE USING gist (connector_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
                WHERE (state IN ('active', 'consumed'));
                ALTER TABLE charging.reservations
                ADD CONSTRAINT ex_reservations_driver_slot
                EXCLUDE USING gist (driver_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
                WHERE (state IN ('active', 'consumed'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_sessions_connector_claim",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ux_sessions_reservation_claim",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ix_reservations_ends_at_id",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservations_cancelled_at",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservations_expired_at",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_delivery_attempts_capture",
                schema: "charging",
                table: "notification_delivery_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_delivery_attempts_evidence",
                schema: "charging",
                table: "notification_delivery_attempts");

            migrationBuilder.Sql("""
                ALTER TABLE charging.reservations DROP CONSTRAINT ex_reservations_connector_slot;
                ALTER TABLE charging.reservations DROP CONSTRAINT ex_reservations_driver_slot;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE charging.sessions ALTER COLUMN lifecycle
                    TYPE integer USING (CASE lifecycle
                        WHEN 'starting' THEN '0'
                        WHEN 'active' THEN '1'
                        WHEN 'stopping' THEN '2'
                        WHEN 'completed' THEN '3'
                        WHEN 'failed' THEN '4'
                        ELSE lifecycle::text END)::integer;
                ALTER TABLE charging.reservations ALTER COLUMN state
                    TYPE integer USING (CASE state
                        WHEN 'active' THEN '0'
                        WHEN 'consumed' THEN '1'
                        WHEN 'cancelled' THEN '2'
                        WHEN 'expired' THEN '3'
                        ELSE state::text END)::integer;
                ALTER TABLE charging.reservation_notifications ALTER COLUMN kind
                    TYPE integer USING (CASE kind
                        WHEN 'created' THEN '0'
                        WHEN 'cancelled' THEN '1'
                        WHEN 'expired' THEN '2'
                        ELSE kind::text END)::integer;
                ALTER TABLE charging.operations ALTER COLUMN outcome
                    TYPE integer USING (CASE outcome
                        WHEN 'pending' THEN '0'
                        WHEN 'confirmed' THEN '1'
                        WHEN 'rejected' THEN '2'
                        WHEN 'uncertain' THEN '3'
                        ELSE outcome::text END)::integer;
                ALTER TABLE charging.operations ALTER COLUMN kind
                    TYPE integer USING (CASE kind
                        WHEN 'start' THEN '0'
                        WHEN 'stop' THEN '1'
                        ELSE kind::text END)::integer;
                ALTER TABLE charging.notification_delivery_attempts ALTER COLUMN outcome
                    TYPE integer USING (CASE outcome
                        WHEN 'uncertain' THEN '1'
                        WHEN 'captured' THEN '2'
                        WHEN 'failed' THEN '3'
                        WHEN 'not_captured' THEN '4'
                        ELSE outcome::text END)::integer;
                ALTER TABLE charging.file_metadata ALTER COLUMN state
                    TYPE integer USING (CASE state
                        WHEN 'awaiting_upload' THEN '0'
                        WHEN 'uploaded' THEN '1'
                        WHEN 'processed' THEN '2'
                        WHEN 'published' THEN '3'
                        ELSE state::text END)::integer;
                ALTER TABLE charging.file_metadata ALTER COLUMN kind
                    TYPE integer USING (CASE kind
                        WHEN 'station_original' THEN '0'
                        WHEN 'station_variant' THEN '1'
                        WHEN 'session_summary' THEN '2'
                        WHEN 'diagnostic_bundle' THEN '3'
                        ELSE kind::text END)::integer;
                ALTER TABLE charging.attempts ALTER COLUMN outcome
                    TYPE integer USING (CASE outcome
                        WHEN 'pending' THEN '0'
                        WHEN 'confirmed' THEN '1'
                        WHEN 'rejected' THEN '2'
                        WHEN 'uncertain' THEN '3'
                        ELSE outcome::text END)::integer;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_sessions_connector_claim",
                schema: "charging",
                table: "sessions",
                column: "connector_id",
                unique: true,
                filter: "lifecycle IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_sessions_reservation_claim",
                schema: "charging",
                table: "sessions",
                column: "reservation_id",
                unique: true,
                filter: "lifecycle <> 4");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_ends_at_id",
                schema: "charging",
                table: "reservations",
                columns: new[] { "ends_at", "id" },
                filter: "state = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservations_cancelled_at",
                schema: "charging",
                table: "reservations",
                sql: "cancelled_at IS NULL OR (state = 2 AND isfinite(cancelled_at))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservations_expired_at",
                schema: "charging",
                table: "reservations",
                sql: "expired_at IS NULL OR (state = 3 AND isfinite(expired_at))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservation_notifications_kind",
                schema: "charging",
                table: "reservation_notifications",
                sql: "kind IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_delivery_attempts_capture",
                schema: "charging",
                table: "notification_delivery_attempts",
                sql: "(outcome = 2) = (external_message_id IS NOT NULL) AND (external_message_id IS NULL OR length(external_message_id) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_delivery_attempts_evidence",
                schema: "charging",
                table: "notification_delivery_attempts",
                sql: "outcome = 1 OR completed_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_delivery_attempts_outcome",
                schema: "charging",
                table: "notification_delivery_attempts",
                sql: "outcome IN (1, 2, 3, 4)");

            migrationBuilder.Sql("""
                ALTER TABLE charging.reservations
                ADD CONSTRAINT ex_reservations_connector_slot
                EXCLUDE USING gist (connector_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
                WHERE (state IN (0, 1));
                ALTER TABLE charging.reservations
                ADD CONSTRAINT ex_reservations_driver_slot
                EXCLUDE USING gist (driver_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
                WHERE (state IN (0, 1));
                """);

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .OldAnnotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.file_kind", "diagnostic_bundle,session_summary,station_original,station_variant")
                .OldAnnotation("Npgsql:Enum:charging.file_state", "awaiting_upload,processed,published,uploaded")
                .OldAnnotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,created,expired")
                .OldAnnotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .OldAnnotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping");
        }
    }
}
