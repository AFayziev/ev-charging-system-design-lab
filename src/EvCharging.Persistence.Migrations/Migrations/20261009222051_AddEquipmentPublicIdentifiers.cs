using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentPublicIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_evses_charger_id",
                schema: "charging",
                table: "evses");

            migrationBuilder.AddColumn<Guid>(
                name: "public_id",
                schema: "charging",
                table: "stations",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<int>(
                name: "number",
                schema: "charging",
                table: "evses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "public_id",
                schema: "charging",
                table: "evses",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<int>(
                name: "number",
                schema: "charging",
                table: "connectors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "public_id",
                schema: "charging",
                table: "connectors",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "public_id",
                schema: "charging",
                table: "chargers",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.Sql("""
                UPDATE charging.evses AS evse
                SET number = numbered.number
                FROM (
                    SELECT id, row_number() OVER (PARTITION BY charger_id ORDER BY id)::integer AS number
                    FROM charging.evses
                ) AS numbered
                WHERE evse.id = numbered.id;

                UPDATE charging.connectors SET number = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_stations_public_id",
                schema: "charging",
                table: "stations",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evses_charger_id_number",
                schema: "charging",
                table: "evses",
                columns: new[] { "charger_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evses_public_id",
                schema: "charging",
                table: "evses",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connectors_public_id",
                schema: "charging",
                table: "connectors",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chargers_public_id",
                schema: "charging",
                table: "chargers",
                column: "public_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stations_public_id",
                schema: "charging",
                table: "stations");

            migrationBuilder.DropIndex(
                name: "ix_evses_charger_id_number",
                schema: "charging",
                table: "evses");

            migrationBuilder.DropIndex(
                name: "ix_evses_public_id",
                schema: "charging",
                table: "evses");

            migrationBuilder.DropIndex(
                name: "ix_connectors_public_id",
                schema: "charging",
                table: "connectors");

            migrationBuilder.DropIndex(
                name: "ix_chargers_public_id",
                schema: "charging",
                table: "chargers");

            migrationBuilder.DropColumn(
                name: "public_id",
                schema: "charging",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "number",
                schema: "charging",
                table: "evses");

            migrationBuilder.DropColumn(
                name: "public_id",
                schema: "charging",
                table: "evses");

            migrationBuilder.DropColumn(
                name: "number",
                schema: "charging",
                table: "connectors");

            migrationBuilder.DropColumn(
                name: "public_id",
                schema: "charging",
                table: "connectors");

            migrationBuilder.DropColumn(
                name: "public_id",
                schema: "charging",
                table: "chargers");

            migrationBuilder.CreateIndex(
                name: "ix_evses_charger_id",
                schema: "charging",
                table: "evses",
                column: "charger_id");
        }
    }
}
