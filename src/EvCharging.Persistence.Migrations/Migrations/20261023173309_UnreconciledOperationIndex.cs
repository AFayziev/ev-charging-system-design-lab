using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class UnreconciledOperationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_operations_automatic_checks_end_at_id",
                schema: "charging",
                table: "operations",
                columns: new[] { "automatic_checks_end_at", "id" },
                filter: "outcome IN ('pending', 'uncertain') AND authorized_attempt_count > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_operations_automatic_checks_end_at_id",
                schema: "charging",
                table: "operations");
        }
    }
}
