using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SessionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "session_evidence",
                schema: "charging",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    last_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    final_energy_k_wh = table.Column<decimal>(type: "numeric", nullable: true),
                    final_reading_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_evidence", x => x.session_id);
                    table.ForeignKey(
                        name: "fk_session_evidence_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "charging",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.Sql("""
                REVOKE INSERT, UPDATE, DELETE ON charging.session_evidence FROM platform_writer;
                GRANT SELECT, INSERT ON charging.session_evidence TO charging_messaging;
                GRANT UPDATE (last_observed_at, final_energy_k_wh, final_reading_at)
                    ON charging.session_evidence TO charging_messaging;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_evidence",
                schema: "charging");
        }
    }
}
