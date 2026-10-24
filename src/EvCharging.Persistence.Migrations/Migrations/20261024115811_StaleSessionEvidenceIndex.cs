using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class StaleSessionEvidenceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_evidence_read_at",
                schema: "charging",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_next_evidence_read_at_id",
                schema: "charging",
                table: "sessions",
                columns: new[] { "next_evidence_read_at", "id" },
                filter: "lifecycle IN ('active', 'stopping')");

            migrationBuilder.Sql("GRANT UPDATE (next_evidence_read_at) ON charging.sessions TO charging_messaging;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE UPDATE (next_evidence_read_at) ON charging.sessions FROM charging_messaging;");

            migrationBuilder.DropIndex(
                name: "ix_sessions_next_evidence_read_at_id",
                schema: "charging",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "next_evidence_read_at",
                schema: "charging",
                table: "sessions");
        }
    }
}
