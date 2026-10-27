using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReservationAvailabilityIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_reservations_connector_id_ends_at",
                schema: "charging",
                table: "reservations",
                columns: new[] { "connector_id", "ends_at" },
                filter: "state IN ('active', 'consumed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_connector_id_ends_at",
                schema: "charging",
                table: "reservations");
        }
    }
}
