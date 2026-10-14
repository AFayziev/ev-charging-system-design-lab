using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    recipient_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    html_body = table.Column<string>(type: "text", nullable: true),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_content", "length(recipient_email) > 0 AND length(subject) > 0 AND length(body) > 0");
                    table.CheckConstraint("ck_notifications_html_body", "html_body IS NULL OR length(html_body) > 0");
                    table.CheckConstraint("ck_notifications_times", "isfinite(prepared_at)");
                });

            migrationBuilder.CreateTable(
                name: "reservation_notifications",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservation_notifications", x => x.id);
                    table.CheckConstraint("ck_reservation_notifications_kind", "kind IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "fk_reservation_notifications_notifications_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "charging",
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reservation_notifications_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "charging",
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_public_id",
                schema: "charging",
                table: "notifications",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reservation_notifications_notification_id",
                schema: "charging",
                table: "reservation_notifications",
                column: "notification_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reservation_notifications_reservation_id_kind",
                schema: "charging",
                table: "reservation_notifications",
                columns: new[] { "reservation_id", "kind" },
                unique: true);

            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA charging TO notification_messaging;
                GRANT SELECT ON charging.reservations, charging.drivers, charging.connectors,
                    charging.chargers, charging.stations TO notification_messaging;
                REVOKE ALL ON charging.notifications, charging.reservation_notifications
                    FROM platform_writer, platform_reader;
                GRANT SELECT, INSERT ON charging.notifications, charging.reservation_notifications
                    TO notification_messaging;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE SELECT ON charging.reservations, charging.drivers, charging.connectors,
                    charging.chargers, charging.stations FROM notification_messaging;
                REVOKE USAGE ON SCHEMA charging FROM notification_messaging;
                """);

            migrationBuilder.DropTable(
                name: "reservation_notifications",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "charging");
        }
    }
}
