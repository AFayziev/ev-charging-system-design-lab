using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ChargingStartAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_connector_id",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ix_sessions_reservation_id",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ix_operations_driver_id_kind_idempotency_key",
                schema: "charging",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "request_fingerprint",
                schema: "charging",
                table: "operations");

            migrationBuilder.AddColumn<Guid>(
                name: "public_id",
                schema: "charging",
                table: "sessions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.Sql("""
                ALTER TABLE charging.operations
                ALTER COLUMN idempotency_key TYPE uuid USING idempotency_key::uuid;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "public_id",
                schema: "charging",
                table: "operations",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_public_id",
                schema: "charging",
                table: "sessions",
                column: "public_id",
                unique: true);

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
                name: "ix_operations_driver_id_kind_idempotency_key",
                schema: "charging",
                table: "operations",
                columns: new[] { "driver_id", "kind", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operations_public_id",
                schema: "charging",
                table: "operations",
                column: "public_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_public_id",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ux_sessions_connector_claim",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ux_sessions_reservation_claim",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ix_operations_driver_id_kind_idempotency_key",
                schema: "charging",
                table: "operations");

            migrationBuilder.DropIndex(
                name: "ix_operations_public_id",
                schema: "charging",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "public_id",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "public_id",
                schema: "charging",
                table: "operations");

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                schema: "charging",
                table: "operations",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "request_fingerprint",
                schema: "charging",
                table: "operations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_connector_id",
                schema: "charging",
                table: "sessions",
                column: "connector_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_reservation_id",
                schema: "charging",
                table: "sessions",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_operations_driver_id_kind_idempotency_key",
                schema: "charging",
                table: "operations",
                columns: new[] { "driver_id", "kind", "idempotency_key" });
        }
    }
}
