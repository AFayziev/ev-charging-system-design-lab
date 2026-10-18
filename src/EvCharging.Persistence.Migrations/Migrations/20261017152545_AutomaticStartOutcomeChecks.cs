using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticStartOutcomeChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "automatic_checks_end_at",
                schema: "charging",
                table: "operations",
                type: "timestamp with time zone",
                nullable: true);
            migrationBuilder.Sql("GRANT UPDATE (automatic_checks_end_at) ON charging.operations TO charging_messaging;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE UPDATE (automatic_checks_end_at) ON charging.operations FROM charging_messaging;");
            migrationBuilder.DropColumn(
                name: "automatic_checks_end_at",
                schema: "charging",
                table: "operations");
        }
    }
}
