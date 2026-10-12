using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReservationExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deadlines",
                schema: "charging");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_ends_at_id",
                schema: "charging",
                table: "reservations",
                columns: new[] { "ends_at", "id" },
                filter: "state = 0");

            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA charging TO charging_messaging;
                GRANT SELECT ON charging.reservations, charging.drivers, charging.sessions TO charging_messaging;
                GRANT UPDATE (state) ON charging.reservations TO charging_messaging;
                GRANT UPDATE (id) ON charging.drivers TO charging_messaging;
                GRANT SELECT, INSERT ON charging.audit_entries TO charging_messaging;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE SELECT ON charging.reservations, charging.drivers, charging.sessions FROM charging_messaging;
                REVOKE UPDATE (state) ON charging.reservations FROM charging_messaging;
                REVOKE UPDATE (id) ON charging.drivers FROM charging_messaging;
                REVOKE SELECT, INSERT ON charging.audit_entries FROM charging_messaging;
                REVOKE USAGE ON SCHEMA charging FROM charging_messaging;
                """);

            migrationBuilder.DropIndex(
                name: "ix_reservations_ends_at_id",
                schema: "charging",
                table: "reservations");

            migrationBuilder.CreateTable(
                name: "deadlines",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deadlines", x => x.id);
                    table.ForeignKey(
                        name: "fk_deadlines_operations_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "charging",
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deadlines_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "charging",
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deadlines_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "charging",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deadlines_due_at",
                schema: "charging",
                table: "deadlines",
                column: "due_at",
                filter: "handled_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_deadlines_operation_id",
                schema: "charging",
                table: "deadlines",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_deadlines_reservation_id",
                schema: "charging",
                table: "deadlines",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_deadlines_session_id",
                schema: "charging",
                table: "deadlines",
                column: "session_id");
        }
    }
}
