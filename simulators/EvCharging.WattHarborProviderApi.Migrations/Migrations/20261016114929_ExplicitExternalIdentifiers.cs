using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.WattHarborProviderApi.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ExplicitExternalIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "reference",
                table: "sessions",
                newName: "external_session_id");

            migrationBuilder.RenameIndex(
                name: "ix_sessions_reference",
                table: "sessions",
                newName: "ix_sessions_external_session_id");

            migrationBuilder.RenameColumn(
                name: "session_reference",
                table: "commands",
                newName: "external_session_id");

            migrationBuilder.RenameColumn(
                name: "reference",
                table: "commands",
                newName: "external_operation_id");

            migrationBuilder.RenameIndex(
                name: "ix_commands_reference",
                table: "commands",
                newName: "ix_commands_external_operation_id");

            migrationBuilder.Sql("""
                UPDATE commands SET rejection_code = 'external_session_id_conflict'
                WHERE rejection_code = 'session_reference_conflict';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE commands SET rejection_code = 'session_reference_conflict'
                WHERE rejection_code = 'external_session_id_conflict';
                """);

            migrationBuilder.RenameColumn(
                name: "external_session_id",
                table: "sessions",
                newName: "reference");

            migrationBuilder.RenameIndex(
                name: "ix_sessions_external_session_id",
                table: "sessions",
                newName: "ix_sessions_reference");

            migrationBuilder.RenameColumn(
                name: "external_session_id",
                table: "commands",
                newName: "session_reference");

            migrationBuilder.RenameColumn(
                name: "external_operation_id",
                table: "commands",
                newName: "reference");

            migrationBuilder.RenameIndex(
                name: "ix_commands_external_operation_id",
                table: "commands",
                newName: "ix_commands_reference");
        }
    }
}
