using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReservationLifecycleTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "charging",
                table: "reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expired_at",
                schema: "charging",
                table: "reservations",
                type: "timestamp with time zone",
                nullable: true);

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

            migrationBuilder.Sql("GRANT UPDATE (expired_at) ON charging.reservations TO charging_messaging;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE UPDATE (expired_at) ON charging.reservations FROM charging_messaging;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservations_cancelled_at",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservations_expired_at",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "charging",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "expired_at",
                schema: "charging",
                table: "reservations");
        }
    }
}
