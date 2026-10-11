using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReservationAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_driver_id",
                schema: "charging",
                table: "reservations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "arrival_deadline",
                schema: "charging",
                table: "reservations",
                type: "timestamp with time zone",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_reservations_driver_id_state_ends_at",
                schema: "charging",
                table: "reservations",
                columns: new[] { "driver_id", "state", "ends_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservations_arrival_deadline",
                schema: "charging",
                table: "reservations",
                sql: "isfinite(arrival_deadline) AND starts_at < arrival_deadline AND arrival_deadline <= ends_at");

            migrationBuilder.Sql("""
                ALTER TABLE charging.reservations
                ADD CONSTRAINT ex_reservations_driver_slot
                EXCLUDE USING gist (driver_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
                WHERE (state IN (0, 1));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE charging.reservations DROP CONSTRAINT ex_reservations_driver_slot;");

            migrationBuilder.DropIndex(
                name: "ix_reservations_driver_id_state_ends_at",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservations_arrival_deadline",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "arrival_deadline",
                schema: "charging",
                table: "reservations");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_driver_id",
                schema: "charging",
                table: "reservations",
                column: "driver_id");
        }
    }
}
