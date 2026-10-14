using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDeliveryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_delivery_attempts",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_delivery_attempts", x => x.id);
                    table.CheckConstraint("ck_notification_delivery_attempts_capture", "(outcome = 2) = (external_message_id IS NOT NULL) AND (external_message_id IS NULL OR length(external_message_id) > 0)");
                    table.CheckConstraint("ck_notification_delivery_attempts_evidence", "outcome = 1 OR completed_at IS NOT NULL");
                    table.CheckConstraint("ck_notification_delivery_attempts_number", "number > 0");
                    table.CheckConstraint("ck_notification_delivery_attempts_outcome", "outcome IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_notification_delivery_attempts_provider", "length(provider) > 0");
                    table.CheckConstraint("ck_notification_delivery_attempts_times", "isfinite(started_at) AND (completed_at IS NULL OR (isfinite(completed_at) AND completed_at >= started_at))");
                    table.ForeignKey(
                        name: "fk_notification_delivery_attempts_notifications_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "charging",
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_attempts_notification_id_number",
                schema: "charging",
                table: "notification_delivery_attempts",
                columns: new[] { "notification_id", "number" },
                unique: true);
            migrationBuilder.Sql("""
                REVOKE ALL ON charging.notification_delivery_attempts FROM platform_writer, platform_reader;
                GRANT SELECT, INSERT ON charging.notification_delivery_attempts TO notification_messaging;
                GRANT UPDATE (outcome, completed_at, external_message_id, failure_code)
                    ON charging.notification_delivery_attempts TO notification_messaging;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_delivery_attempts",
                schema: "charging");
        }
    }
}
