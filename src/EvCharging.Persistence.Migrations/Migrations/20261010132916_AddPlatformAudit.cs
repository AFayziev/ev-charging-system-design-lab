using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "charging",
                table: "stations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    resource_type = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<long>(type: "bigint", nullable: false),
                    actor_type = table.Column<string>(type: "text", nullable: false),
                    actor_issuer = table.Column<string>(type: "text", nullable: true),
                    actor_subject = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    details_version = table.Column<int>(type: "integer", nullable: false),
                    trace_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_resource_type_resource_id_occurred_at_id",
                schema: "charging",
                table: "audit_entries",
                columns: new[] { "resource_type", "resource_id", "occurred_at", "id" });

            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON TABLE charging.audit_entries FROM platform_writer;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "charging");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "charging",
                table: "stations");
        }
    }
}
