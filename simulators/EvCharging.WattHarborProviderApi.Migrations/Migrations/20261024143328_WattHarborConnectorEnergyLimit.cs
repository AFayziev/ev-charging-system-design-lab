using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.WattHarborProviderApi.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class WattHarborConnectorEnergyLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "energy_limit_k_wh",
                table: "connectors",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "energy_limit_k_wh",
                table: "connectors");
        }
    }
}
