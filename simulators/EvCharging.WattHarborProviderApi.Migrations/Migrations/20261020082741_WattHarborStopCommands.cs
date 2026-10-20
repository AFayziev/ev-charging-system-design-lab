using System;
using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.WattHarborProviderApi.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class WattHarborStopCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:command_kind", "start,stop");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ended_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "station_id",
                table: "commands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "connector_id",
                table: "commands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "charger_id",
                table: "commands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<CommandKind>(
                name: "kind",
                table: "commands",
                type: "command_kind",
                nullable: false,
                defaultValue: CommandKind.Start);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ended_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "commands");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:Enum:command_kind", "start,stop");

            migrationBuilder.AlterColumn<string>(
                name: "station_id",
                table: "commands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "connector_id",
                table: "commands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "charger_id",
                table: "commands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
