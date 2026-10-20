using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReservationExpiryAtArrivalDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_driver_id_state_ends_at",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "ix_reservations_ends_at_id",
                schema: "charging",
                table: "reservations");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_arrival_deadline_id",
                schema: "charging",
                table: "reservations",
                columns: new[] { "arrival_deadline", "id" },
                filter: "state = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_driver_id_state_arrival_deadline",
                schema: "charging",
                table: "reservations",
                columns: new[] { "driver_id", "state", "arrival_deadline" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_arrival_deadline_id",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "ix_reservations_driver_id_state_arrival_deadline",
                schema: "charging",
                table: "reservations");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_driver_id_state_ends_at",
                schema: "charging",
                table: "reservations",
                columns: new[] { "driver_id", "state", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reservations_ends_at_id",
                schema: "charging",
                table: "reservations",
                columns: new[] { "ends_at", "id" },
                filter: "state = 'active'");
        }
    }
}
