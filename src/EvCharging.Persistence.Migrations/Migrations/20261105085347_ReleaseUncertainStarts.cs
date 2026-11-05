using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseUncertainStarts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "released_at",
                schema: "charging",
                table: "operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_operations_unsettled",
                schema: "charging",
                table: "operations",
                column: "id",
                filter: "outcome IN ('pending', 'uncertain') AND authorized_attempt_count > 0 AND released_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_operations_unsettled",
                schema: "charging",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "released_at",
                schema: "charging",
                table: "operations");
        }
    }
}
