using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SearchCoverPhotoAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_station_media_items_station_id_position",
                schema: "charging",
                table: "station_media_items",
                columns: new[] { "station_id", "position" },
                filter: "state = 'published' AND position IS NOT NULL");

            migrationBuilder.Sql("""
                GRANT SELECT ON charging.station_media_items TO search_messaging;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE SELECT ON charging.station_media_items FROM search_messaging;
                """);

            migrationBuilder.DropIndex(
                name: "ix_station_media_items_station_id_position",
                schema: "charging",
                table: "station_media_items");
        }
    }
}
