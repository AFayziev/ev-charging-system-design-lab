using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.SynchronousChargingProviderApi.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    charger_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    connector_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    power_kw = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    charger_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    connector_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    session_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied = table.Column<bool>(type: "boolean", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commands", x => x.id);
                    table.ForeignKey(
                        name: "fk_commands_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "connectors",
                columns: table => new
                {
                    station_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    charger_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    connector_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    power_kw = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    active_command_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connectors", x => new { x.station_id, x.charger_id, x.connector_id });
                    table.ForeignKey(
                        name: "fk_connectors_commands_active_command_id",
                        column: x => x.active_command_id,
                        principalTable: "commands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commands_public_id",
                table: "commands",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commands_reference",
                table: "commands",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commands_session_id",
                table: "commands",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_connectors_active_command_id",
                table: "connectors",
                column: "active_command_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_public_id",
                table: "sessions",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_reference",
                table: "sessions",
                column: "reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connectors");

            migrationBuilder.DropTable(
                name: "commands");

            migrationBuilder.DropTable(
                name: "sessions");
        }
    }
}
