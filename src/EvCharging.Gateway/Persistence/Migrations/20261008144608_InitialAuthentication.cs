using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EvCharging.Gateway.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialAuthentication : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "gateway_auth");

        migrationBuilder.CreateTable(
            name: "DataProtectionKeys",
            schema: "gateway_auth",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FriendlyName = table.Column<string>(type: "text", nullable: true),
                Xml = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Sessions",
            schema: "gateway_auth",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ProtectedTicket = table.Column<byte[]>(type: "bytea", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Sessions", x => x.Id);
            });

        migrationBuilder.Sql("""GRANT SELECT, INSERT, UPDATE, DELETE ON gateway_auth."Sessions" TO gateway_auth_runtime; GRANT SELECT, INSERT ON gateway_auth."DataProtectionKeys" TO gateway_auth_runtime; GRANT USAGE ON SEQUENCE gateway_auth."DataProtectionKeys_Id_seq" TO gateway_auth_runtime;""");

        migrationBuilder.CreateIndex(
            name: "IX_Sessions_ExpiresAt",
            schema: "gateway_auth",
            table: "Sessions",
            column: "ExpiresAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DataProtectionKeys",
            schema: "gateway_auth");

        migrationBuilder.DropTable(
            name: "Sessions",
            schema: "gateway_auth");
    }
}
