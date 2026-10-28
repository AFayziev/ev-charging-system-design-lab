using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class StationMediaGallery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_metadata",
                schema: "charging");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .Annotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .Annotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .Annotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,charging_completed,created,expired")
                .Annotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .Annotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping")
                .Annotation("Npgsql:Enum:charging.station_media_kind", "photo")
                .Annotation("Npgsql:Enum:charging.station_media_version_state", "awaiting_upload,failed,uploaded")
                .OldAnnotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .OldAnnotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.file_kind", "diagnostic_bundle,session_summary,station_original,station_variant")
                .OldAnnotation("Npgsql:Enum:charging.file_state", "awaiting_upload,processed,published,uploaded")
                .OldAnnotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,charging_completed,created,expired")
                .OldAnnotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .OldAnnotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping");

            migrationBuilder.AddColumn<int>(
                name: "gallery_version",
                schema: "charging",
                table: "stations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "station_media_items",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "charging.station_media_kind", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_station_media_items", x => x.id);
                    table.CheckConstraint("ck_station_media_items_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_station_media_items_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "charging",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "station_media_versions",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    state = table.Column<string>(type: "charging.station_media_version_state", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    upload_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_station_media_versions", x => x.id);
                    table.CheckConstraint("ck_station_media_versions_byte_length", "byte_length > 0");
                    table.CheckConstraint("ck_station_media_versions_upload_expires_at", "isfinite(upload_expires_at)");
                    table.CheckConstraint("ck_station_media_versions_uploaded_at", "(state = 'uploaded') = (uploaded_at IS NOT NULL) AND (uploaded_at IS NULL OR isfinite(uploaded_at))");
                    table.ForeignKey(
                        name: "fk_station_media_versions_station_media_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "charging",
                        principalTable: "station_media_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stations_gallery_version",
                schema: "charging",
                table: "stations",
                sql: "gallery_version >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_station_media_items_public_id",
                schema: "charging",
                table: "station_media_items",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_station_media_items_station_id_position",
                schema: "charging",
                table: "station_media_items",
                columns: new[] { "station_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_station_media_versions_item_id",
                schema: "charging",
                table: "station_media_versions",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_station_media_versions_public_id",
                schema: "charging",
                table: "station_media_versions",
                column: "public_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "station_media_versions",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "station_media_items",
                schema: "charging");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stations_gallery_version",
                schema: "charging",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "gallery_version",
                schema: "charging",
                table: "stations");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .Annotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .Annotation("Npgsql:Enum:charging.file_kind", "diagnostic_bundle,session_summary,station_original,station_variant")
                .Annotation("Npgsql:Enum:charging.file_state", "awaiting_upload,processed,published,uploaded")
                .Annotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .Annotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,charging_completed,created,expired")
                .Annotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .Annotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping")
                .OldAnnotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .OldAnnotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,charging_completed,created,expired")
                .OldAnnotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .OldAnnotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping")
                .OldAnnotation("Npgsql:Enum:charging.station_media_kind", "photo")
                .OldAnnotation("Npgsql:Enum:charging.station_media_version_state", "awaiting_upload,failed,uploaded");

            migrationBuilder.CreateTable(
                name: "file_metadata",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bucket = table.Column<string>(type: "text", nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: true),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "charging.file_kind", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<string>(type: "charging.file_state", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_metadata", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_metadata_file_metadata_source_file_id",
                        column: x => x.source_file_id,
                        principalSchema: "charging",
                        principalTable: "file_metadata",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_metadata_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "charging",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_metadata_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "charging",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_file_metadata_bucket_object_key",
                schema: "charging",
                table: "file_metadata",
                columns: new[] { "bucket", "object_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_file_metadata_session_id",
                schema: "charging",
                table: "file_metadata",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_metadata_source_file_id",
                schema: "charging",
                table: "file_metadata",
                column: "source_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_metadata_station_id",
                schema: "charging",
                table: "file_metadata",
                column: "station_id");
        }
    }
}
