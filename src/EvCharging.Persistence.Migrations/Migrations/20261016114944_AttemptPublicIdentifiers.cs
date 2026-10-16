using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AttemptPublicIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "session_reference",
                schema: "charging",
                table: "attempts",
                newName: "session_public_id");

            migrationBuilder.RenameColumn(
                name: "operation_reference",
                schema: "charging",
                table: "attempts",
                newName: "operation_public_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "session_public_id",
                schema: "charging",
                table: "attempts",
                newName: "session_reference");

            migrationBuilder.RenameColumn(
                name: "operation_public_id",
                schema: "charging",
                table: "attempts",
                newName: "operation_reference");
        }
    }
}
