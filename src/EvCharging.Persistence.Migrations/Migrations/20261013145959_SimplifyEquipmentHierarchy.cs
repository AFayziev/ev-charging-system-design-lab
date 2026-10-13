using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyEquipmentHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_connectors_evses_evse_id",
                schema: "charging",
                table: "connectors");

            migrationBuilder.DropForeignKey(
                name: "fk_provider_connectors_provider_evses_provider_evse_id",
                schema: "charging",
                table: "provider_connectors");

            migrationBuilder.DropIndex(
                name: "ix_provider_connectors_connector_id",
                schema: "charging",
                table: "provider_connectors");

            migrationBuilder.DropIndex(
                name: "ix_provider_connectors_provider_evse_id_external_id",
                schema: "charging",
                table: "provider_connectors");

            migrationBuilder.DropIndex(
                name: "ix_provider_chargers_charger_id",
                schema: "charging",
                table: "provider_chargers");

            migrationBuilder.DropIndex(
                name: "ix_connectors_evse_id",
                schema: "charging",
                table: "connectors");

            migrationBuilder.RenameColumn(
                name: "provider_evse_id",
                schema: "charging",
                table: "provider_connectors",
                newName: "provider_charger_id");

            migrationBuilder.RenameColumn(
                name: "evse_id",
                schema: "charging",
                table: "connectors",
                newName: "charger_id");

            migrationBuilder.AddColumn<string>(
                name: "external_evse_id",
                schema: "charging",
                table: "provider_connectors",
                type: "text",
                nullable: true);

            // Resolve the old relationships before removing their address records.
            migrationBuilder.Sql("""
                UPDATE charging.connectors AS connector
                SET charger_id = evse.charger_id, number = evse.number
                FROM charging.evses AS evse
                WHERE connector.charger_id = evse.id;

                UPDATE charging.provider_connectors AS connector
                SET provider_charger_id = evse.provider_charger_id, external_evse_id = evse.external_id
                FROM charging.provider_evses AS evse
                WHERE connector.provider_charger_id = evse.id;
                """);

            migrationBuilder.DropTable(
                name: "provider_evses",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "evses",
                schema: "charging");

            migrationBuilder.CreateIndex(
                name: "ix_provider_connectors_connector_id",
                schema: "charging",
                table: "provider_connectors",
                column: "connector_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_connectors_provider_charger_id_external_evse_id_ex",
                schema: "charging",
                table: "provider_connectors",
                columns: new[] { "provider_charger_id", "external_evse_id", "external_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_provider_chargers_charger_id",
                schema: "charging",
                table: "provider_chargers",
                column: "charger_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connectors_charger_id_number",
                schema: "charging",
                table: "connectors",
                columns: new[] { "charger_id", "number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_connectors_chargers_charger_id",
                schema: "charging",
                table: "connectors",
                column: "charger_id",
                principalSchema: "charging",
                principalTable: "chargers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_provider_connectors_provider_chargers_provider_charger_id",
                schema: "charging",
                table: "provider_connectors",
                column: "provider_charger_id",
                principalSchema: "charging",
                principalTable: "provider_chargers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException(
                "Simplified equipment cannot restore the removed EVSE identities. Restore a backup to roll back.");
    }
}
