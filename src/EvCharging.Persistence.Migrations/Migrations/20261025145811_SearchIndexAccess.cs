using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SearchIndexAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA charging TO search_messaging;
                GRANT SELECT ON charging.stations, charging.chargers, charging.connectors TO search_messaging;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE SELECT ON charging.stations, charging.chargers, charging.connectors FROM search_messaging;
                REVOKE USAGE ON SCHEMA charging FROM search_messaging;
                """);
        }
    }
}
