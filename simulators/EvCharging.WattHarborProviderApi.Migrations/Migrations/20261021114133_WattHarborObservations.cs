using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.WattHarborProviderApi.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class WattHarborObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "callback_targets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    api_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_callback_targets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    power_kw = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    energy_wh = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    is_final_energy = table.Column<bool>(type: "boolean", nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status_code = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observations", x => x.id);
                    table.ForeignKey(
                        name: "fk_observations_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_observations_delivery_status_next_attempt_at",
                table: "observations",
                columns: new[] { "delivery_status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_observations_event_id",
                table: "observations",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_observations_session_id_occurred_at",
                table: "observations",
                columns: new[] { "session_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "callback_targets");

            migrationBuilder.DropTable(
                name: "observations");
        }
    }
}
