using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EvCharging.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gateway_auth");

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "gateway_auth",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "gateway_auth",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_ticket = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    refresh_claim = table.Column<Guid>(type: "uuid", nullable: true),
                    refresh_lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_expires_at",
                schema: "gateway_auth",
                table: "sessions",
                column: "expires_at");

            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE, DELETE ON gateway_auth.sessions TO gateway_auth_runtime;
                GRANT SELECT, INSERT ON gateway_auth.data_protection_keys TO gateway_auth_runtime;
                GRANT USAGE ON SEQUENCE gateway_auth.data_protection_keys_id_seq TO gateway_auth_runtime;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "gateway_auth");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "gateway_auth");
        }
    }
}
