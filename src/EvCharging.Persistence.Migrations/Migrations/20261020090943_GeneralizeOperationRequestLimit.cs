using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeOperationRequestLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_start_request_budget",
                schema: "charging",
                table: "operations");

            migrationBuilder.RenameColumn(
                name: "start_request_limit",
                schema: "charging",
                table: "operations",
                newName: "request_limit");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_request_budget",
                schema: "charging",
                table: "operations",
                sql: "authorized_attempt_count >= 0 AND (request_limit IS NULL AND authorized_attempt_count = 0 OR request_limit IS NOT NULL AND request_limit > 0 AND authorized_attempt_count <= request_limit)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_request_budget",
                schema: "charging",
                table: "operations");

            migrationBuilder.RenameColumn(
                name: "request_limit",
                schema: "charging",
                table: "operations",
                newName: "start_request_limit");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_start_request_budget",
                schema: "charging",
                table: "operations",
                sql: "authorized_attempt_count >= 0 AND (start_request_limit IS NULL AND authorized_attempt_count = 0 OR start_request_limit IS NOT NULL AND start_request_limit > 0 AND authorized_attempt_count <= start_request_limit)");
        }
    }
}
