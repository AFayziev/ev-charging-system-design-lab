using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ChargingStartExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "authorized_attempt_count",
                schema: "charging",
                table: "operations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "effect_attempt_limit",
                schema: "charging",
                table: "operations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "integration_key",
                schema: "charging",
                table: "charging_providers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "charging_provider_id",
                schema: "charging",
                table: "attempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "integration_key",
                schema: "charging",
                table: "attempts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "operation_reference",
                schema: "charging",
                table: "attempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provider_applied_at",
                schema: "charging",
                table: "attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_charger_id",
                schema: "charging",
                table: "attempts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "provider_command_id",
                schema: "charging",
                table: "attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_connector_id",
                schema: "charging",
                table: "attempts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "provider_session_id",
                schema: "charging",
                table: "attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_station_id",
                schema: "charging",
                table: "attempts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "response_body",
                schema: "charging",
                table: "attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "response_status_code",
                schema: "charging",
                table: "attempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "session_reference",
                schema: "charging",
                table: "attempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_effect_budget",
                schema: "charging",
                table: "operations",
                sql: "authorized_attempt_count >= 0 AND (effect_attempt_limit IS NULL AND authorized_attempt_count = 0 OR effect_attempt_limit IS NOT NULL AND effect_attempt_limit > 0 AND authorized_attempt_count <= effect_attempt_limit)");
            migrationBuilder.Sql("""
                GRANT SELECT ON charging.operations, charging.attempts, charging.charging_providers,
                    charging.provider_stations, charging.provider_chargers, charging.provider_connectors
                    TO charging_messaging;
                GRANT UPDATE (authorized_attempt_count, effect_attempt_limit, outcome)
                    ON charging.operations TO charging_messaging;
                GRANT UPDATE (lifecycle, charging_provider_id, provider_session_id, started_at)
                    ON charging.sessions TO charging_messaging;
                GRANT INSERT ON charging.attempts TO charging_messaging;
                GRANT UPDATE (finished_at, outcome, failure_code, provider_command_id, provider_session_id,
                    provider_applied_at, response_body, response_status_code) ON charging.attempts TO charging_messaging;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE SELECT ON charging.operations, charging.attempts, charging.charging_providers,
                    charging.provider_stations, charging.provider_chargers, charging.provider_connectors
                    FROM charging_messaging;
                REVOKE UPDATE (authorized_attempt_count, effect_attempt_limit, outcome)
                    ON charging.operations FROM charging_messaging;
                REVOKE UPDATE (lifecycle, charging_provider_id, provider_session_id, started_at)
                    ON charging.sessions FROM charging_messaging;
                REVOKE INSERT ON charging.attempts FROM charging_messaging;
                REVOKE UPDATE (finished_at, outcome, failure_code, provider_command_id, provider_session_id,
                    provider_applied_at, response_body, response_status_code) ON charging.attempts FROM charging_messaging;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_effect_budget",
                schema: "charging",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "authorized_attempt_count",
                schema: "charging",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "effect_attempt_limit",
                schema: "charging",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "integration_key",
                schema: "charging",
                table: "charging_providers");

            migrationBuilder.DropColumn(
                name: "charging_provider_id",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "integration_key",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "operation_reference",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "provider_applied_at",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "provider_charger_id",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "provider_command_id",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "provider_connector_id",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "provider_session_id",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "provider_station_id",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "response_body",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "response_status_code",
                schema: "charging",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "session_reference",
                schema: "charging",
                table: "attempts");
        }
    }
}
