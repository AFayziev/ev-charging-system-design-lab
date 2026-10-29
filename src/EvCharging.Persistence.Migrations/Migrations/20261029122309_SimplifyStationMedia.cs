using System;
using EvCharging.Domain.Media;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyStationMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nothing from the versioned gallery was ever processed or published, so no visitor sees these rows;
            // operators upload again. Audit rows of the dropped table could no longer be read back.
            migrationBuilder.Sql("""
                DELETE FROM charging.audit_entries WHERE resource_type = 'StationMediaVersion';
                DELETE FROM charging.station_media_versions;
                DELETE FROM charging.station_media_items;
                """);

            migrationBuilder.DropTable(
                name: "station_media_versions",
                schema: "charging");

            migrationBuilder.DropIndex(
                name: "ix_station_media_items_station_id_position",
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
                .OldAnnotation("Npgsql:Enum:charging.station_media_kind", "photo")
                .OldAnnotation("Npgsql:Enum:charging.station_media_version_state", "awaiting_upload,failed,uploaded");

            migrationBuilder.AlterColumn<int>(
                name: "position",
                schema: "charging",
                table: "station_media_items",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<long>(
                name: "byte_length",
                schema: "charging",
                table: "station_media_items",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "content_type",
                schema: "charging",
                table: "station_media_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "failure_code",
                schema: "charging",
                table: "station_media_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "charging",
                table: "station_media_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaces_item_id",
                schema: "charging",
                table: "station_media_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<StationMediaState>(
                name: "state",
                schema: "charging",
                table: "station_media_items",
                type: "charging.station_media_state",
                nullable: false,
                defaultValue: StationMediaState.Uploading);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "upload_expires_at",
                schema: "charging",
                table: "station_media_items",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "charging",
                table: "station_media_items",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "ix_station_media_items_replaces_item_id",
                schema: "charging",
                table: "station_media_items",
                column: "replaces_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_station_media_items_station_id",
                schema: "charging",
                table: "station_media_items",
                column: "station_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_media_items_byte_length",
                schema: "charging",
                table: "station_media_items",
                sql: "byte_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_media_items_failure_code",
                schema: "charging",
                table: "station_media_items",
                sql: "(state = 'failed') = (failure_code IS NOT NULL) AND (failure_code IS NULL OR failure_code IN ('upload_missing', 'upload_expired', 'undecodable', 'too_many_pixels', 'unsupported_format'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_media_items_published_at",
                schema: "charging",
                table: "station_media_items",
                sql: "(state = 'published') = (published_at IS NOT NULL) AND (published_at IS NULL OR isfinite(published_at))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_media_items_replacement_position",
                schema: "charging",
                table: "station_media_items",
                sql: "(position IS NULL) = (replaces_item_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_media_items_upload_expires_at",
                schema: "charging",
                table: "station_media_items",
                sql: "isfinite(upload_expires_at)");

            migrationBuilder.AddForeignKey(
                name: "fk_station_media_items_station_media_items_replaces_item_id",
                schema: "charging",
                table: "station_media_items",
                column: "replaces_item_id",
                principalSchema: "charging",
                principalTable: "station_media_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // The new columns need defaults only while the table could hold rows.
            migrationBuilder.Sql("""
                ALTER TABLE charging.station_media_items
                    ALTER COLUMN byte_length DROP DEFAULT,
                    ALTER COLUMN content_type DROP DEFAULT,
                    ALTER COLUMN state DROP DEFAULT,
                    ALTER COLUMN upload_expires_at DROP DEFAULT;
                GRANT USAGE ON SCHEMA charging TO media_messaging;
                GRANT SELECT ON charging.stations, charging.station_media_items TO media_messaging;
                GRANT UPDATE (state, failure_code, published_at, position, replaces_item_id)
                    ON charging.station_media_items TO media_messaging;
                GRANT DELETE ON charging.station_media_items TO media_messaging;
                GRANT SELECT, INSERT ON charging.audit_entries TO media_messaging;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE SELECT, INSERT ON charging.audit_entries FROM media_messaging;
                REVOKE UPDATE, DELETE ON charging.station_media_items FROM media_messaging;
                REVOKE SELECT ON charging.stations, charging.station_media_items FROM media_messaging;
                REVOKE USAGE ON SCHEMA charging FROM media_messaging;
                DELETE FROM charging.station_media_items;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_station_media_items_station_media_items_replaces_item_id",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropIndex(
                name: "ix_station_media_items_replaces_item_id",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropIndex(
                name: "ix_station_media_items_station_id",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_station_media_items_byte_length",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_station_media_items_failure_code",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_station_media_items_published_at",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_station_media_items_replacement_position",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_station_media_items_upload_expires_at",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropColumn(
                name: "byte_length",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropColumn(
                name: "content_type",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropColumn(
                name: "failure_code",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropColumn(
                name: "replaces_item_id",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropColumn(
                name: "upload_expires_at",
                schema: "charging",
                table: "station_media_items");

            migrationBuilder.DropColumn(
                name: "xmin",
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
                .Annotation("Npgsql:Enum:charging.station_media_version_state", "awaiting_upload,failed,uploaded")
                .OldAnnotation("Npgsql:Enum:charging.charging_command_kind", "start,stop")
                .OldAnnotation("Npgsql:Enum:charging.command_outcome", "confirmed,pending,rejected,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.notification_delivery_outcome", "captured,failed,not_captured,uncertain")
                .OldAnnotation("Npgsql:Enum:charging.reservation_notification_kind", "cancelled,charging_completed,created,expired")
                .OldAnnotation("Npgsql:Enum:charging.reservation_state", "active,cancelled,consumed,expired")
                .OldAnnotation("Npgsql:Enum:charging.session_lifecycle", "active,completed,failed,starting,stopping")
                .OldAnnotation("Npgsql:Enum:charging.station_media_kind", "photo")
                .OldAnnotation("Npgsql:Enum:charging.station_media_state", "failed,processing,published,uploading");

            migrationBuilder.AlterColumn<int>(
                name: "position",
                schema: "charging",
                table: "station_media_items",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "station_media_versions",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    state = table.Column<int>(type: "charging.station_media_version_state", nullable: false),
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
    }
}
