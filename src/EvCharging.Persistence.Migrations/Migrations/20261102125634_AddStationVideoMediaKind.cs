using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddStationVideoMediaKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_station_media_items_failure_code",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .Annotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .Annotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .Annotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,charging_completed,created,expired")
                .Annotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .Annotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping")
                .Annotation("Npgsql:Enum:charging.station_media_kind", "photo,video")
                .Annotation("Npgsql:Enum:charging.station_media_state", "failed,processing,published,uploading")
                .OldAnnotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .OldAnnotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,charging_completed,created,expired")
                .OldAnnotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .OldAnnotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping")
                .OldAnnotation("Npgsql:Enum:charging.station_media_kind", "photo")
                .OldAnnotation("Npgsql:Enum:charging.station_media_state", "failed,processing,published,uploading");

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_media_items_failure_code",
                schema: "charging",
                table: "station_media_items",
                sql: "(state = 'failed') = (failure_code IS NOT NULL) AND (failure_code IS NULL OR failure_code IN ('upload_missing', 'upload_expired', 'undecodable', 'too_many_pixels', 'unsupported_format', 'too_long', 'unsupported_codec'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_station_media_items_failure_code",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .Annotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .Annotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .Annotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,charging_completed,created,expired")
                .Annotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .Annotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping")
                .Annotation("Npgsql:Enum:charging.station_media_kind", "photo")
                .Annotation("Npgsql:Enum:charging.station_media_state", "failed,processing,published,uploading")
                .OldAnnotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .OldAnnotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,charging_completed,created,expired")
                .OldAnnotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .OldAnnotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping")
                .OldAnnotation("Npgsql:Enum:charging.station_media_kind", "photo,video")
                .OldAnnotation("Npgsql:Enum:charging.station_media_state", "failed,processing,published,uploading");

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_media_items_failure_code",
                schema: "charging",
                table: "station_media_items",
                sql: "(state = 'failed') = (failure_code IS NOT NULL) AND (failure_code IS NULL OR failure_code IN ('upload_missing', 'upload_expired', 'undecodable', 'too_many_pixels', 'unsupported_format'))");
        }
    }
}
