using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RenameStartRequestLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_effect_budget",
                schema: "charging",
                table: "operations");

            migrationBuilder.RenameColumn(
                name: "effect_attempt_limit",
                schema: "charging",
                table: "operations",
                newName: "start_request_limit");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_start_request_budget",
                schema: "charging",
                table: "operations",
                sql: "authorized_attempt_count >= 0 AND (start_request_limit IS NULL AND authorized_attempt_count = 0 OR start_request_limit IS NOT NULL AND start_request_limit > 0 AND authorized_attempt_count <= start_request_limit)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_start_request_budget",
                schema: "charging",
                table: "operations");

            migrationBuilder.RenameColumn(
                name: "start_request_limit",
                schema: "charging",
                table: "operations",
                newName: "effect_attempt_limit");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_effect_budget",
                schema: "charging",
                table: "operations",
                sql: "authorized_attempt_count >= 0 AND (effect_attempt_limit IS NULL AND authorized_attempt_count = 0 OR effect_attempt_limit IS NOT NULL AND effect_attempt_limit > 0 AND authorized_attempt_count <= effect_attempt_limit)");
        }
    }
}
