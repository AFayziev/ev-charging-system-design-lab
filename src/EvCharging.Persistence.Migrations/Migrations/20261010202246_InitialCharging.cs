using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvCharging.Persistence.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialCharging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "charging");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    resource_type = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "text", nullable: false),
                    actor_issuer = table.Column<string>(type: "text", nullable: true),
                    actor_subject = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    details_version = table.Column<int>(type: "integer", nullable: false),
                    trace_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "charging_providers",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charging_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drivers",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_issuer = table.Column<string>(type: "text", nullable: false),
                    identity_subject = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drivers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operator_users",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_issuer = table.Column<string>(type: "text", nullable: false),
                    identity_subject = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric", nullable: false),
                    longitude = table.Column<decimal>(type: "numeric", nullable: false),
                    time_zone_id = table.Column<string>(type: "text", nullable: false),
                    access_instructions = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chargers",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chargers", x => x.id);
                    table.ForeignKey(
                        name: "fk_chargers_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "charging",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operator_assignments",
                schema: "charging",
                columns: table => new
                {
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_assignments", x => new { x.operator_user_id, x.station_id });
                    table.ForeignKey(
                        name: "fk_operator_assignments_operator_users_operator_user_id",
                        column: x => x.operator_user_id,
                        principalSchema: "charging",
                        principalTable: "operator_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operator_assignments_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "charging",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_stations",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charging_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_stations", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_stations_charging_providers_charging_provider_id",
                        column: x => x.charging_provider_id,
                        principalSchema: "charging",
                        principalTable: "charging_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_stations_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "charging",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evses",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    charger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evses", x => x.id);
                    table.ForeignKey(
                        name: "fk_evses_chargers_charger_id",
                        column: x => x.charger_id,
                        principalSchema: "charging",
                        principalTable: "chargers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_chargers",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_chargers", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_chargers_chargers_charger_id",
                        column: x => x.charger_id,
                        principalSchema: "charging",
                        principalTable: "chargers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_chargers_provider_stations_provider_station_id",
                        column: x => x.provider_station_id,
                        principalSchema: "charging",
                        principalTable: "provider_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "connectors",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    evse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    connector_type = table.Column<string>(type: "text", nullable: false),
                    power_supply = table.Column<string>(type: "text", nullable: false),
                    rated_power_kw = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connectors", x => x.id);
                    table.ForeignKey(
                        name: "fk_connectors_evses_evse_id",
                        column: x => x.evse_id,
                        principalSchema: "charging",
                        principalTable: "evses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_evses",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_charger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_evses", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_evses_evses_evse_id",
                        column: x => x.evse_id,
                        principalSchema: "charging",
                        principalTable: "evses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_evses_provider_chargers_provider_charger_id",
                        column: x => x.provider_charger_id,
                        principalSchema: "charging",
                        principalTable: "provider_chargers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    connector_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservations", x => x.id);
                    table.CheckConstraint("ck_reservations_slot", "isfinite(starts_at) AND isfinite(ends_at) AND starts_at < ends_at");
                    table.ForeignKey(
                        name: "fk_reservations_connectors_connector_id",
                        column: x => x.connector_id,
                        principalSchema: "charging",
                        principalTable: "connectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reservations_drivers_driver_id",
                        column: x => x.driver_id,
                        principalSchema: "charging",
                        principalTable: "drivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_connectors",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_evse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_connectors", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_connectors_connectors_connector_id",
                        column: x => x.connector_id,
                        principalSchema: "charging",
                        principalTable: "connectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_connectors_provider_evses_provider_evse_id",
                        column: x => x.provider_evse_id,
                        principalSchema: "charging",
                        principalTable: "provider_evses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifecycle = table.Column<int>(type: "integer", nullable: false),
                    charging_provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_session_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    final_energy_k_wh = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_charging_providers_charging_provider_id",
                        column: x => x.charging_provider_id,
                        principalSchema: "charging",
                        principalTable: "charging_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessions_connectors_connector_id",
                        column: x => x.connector_id,
                        principalSchema: "charging",
                        principalTable: "connectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessions_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "charging",
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_metadata",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bucket = table.Column<string>(type: "text", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "operations",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    request_fingerprint = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_operations_drivers_driver_id",
                        column: x => x.driver_id,
                        principalSchema: "charging",
                        principalTable: "drivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operations_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "charging",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attempts",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<int>(type: "integer", nullable: true),
                    failure_code = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_attempts_operations_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "charging",
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deadlines",
                schema: "charging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deadlines", x => x.id);
                    table.ForeignKey(
                        name: "fk_deadlines_operations_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "charging",
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deadlines_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "charging",
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deadlines_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "charging",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attempts_operation_id_number",
                schema: "charging",
                table: "attempts",
                columns: new[] { "operation_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_resource_type_resource_id_occurred_at_id",
                schema: "charging",
                table: "audit_entries",
                columns: new[] { "resource_type", "resource_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_chargers_public_id",
                schema: "charging",
                table: "chargers",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chargers_station_id",
                schema: "charging",
                table: "chargers",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "ix_connectors_evse_id",
                schema: "charging",
                table: "connectors",
                column: "evse_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connectors_public_id",
                schema: "charging",
                table: "connectors",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deadlines_due_at",
                schema: "charging",
                table: "deadlines",
                column: "due_at",
                filter: "handled_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_deadlines_operation_id",
                schema: "charging",
                table: "deadlines",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_deadlines_reservation_id",
                schema: "charging",
                table: "deadlines",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_deadlines_session_id",
                schema: "charging",
                table: "deadlines",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_drivers_identity_issuer_identity_subject",
                schema: "charging",
                table: "drivers",
                columns: new[] { "identity_issuer", "identity_subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evses_charger_id_number",
                schema: "charging",
                table: "evses",
                columns: new[] { "charger_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evses_public_id",
                schema: "charging",
                table: "evses",
                column: "public_id",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_operations_driver_id_kind_idempotency_key",
                schema: "charging",
                table: "operations",
                columns: new[] { "driver_id", "kind", "idempotency_key" });

            migrationBuilder.CreateIndex(
                name: "ix_operations_session_id",
                schema: "charging",
                table: "operations",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_assignments_station_id",
                schema: "charging",
                table: "operator_assignments",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_users_identity_issuer_identity_subject",
                schema: "charging",
                table: "operator_users",
                columns: new[] { "identity_issuer", "identity_subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_chargers_charger_id",
                schema: "charging",
                table: "provider_chargers",
                column: "charger_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_chargers_provider_station_id_external_id",
                schema: "charging",
                table: "provider_chargers",
                columns: new[] { "provider_station_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_connectors_connector_id",
                schema: "charging",
                table: "provider_connectors",
                column: "connector_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_connectors_provider_evse_id_external_id",
                schema: "charging",
                table: "provider_connectors",
                columns: new[] { "provider_evse_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_evses_evse_id",
                schema: "charging",
                table: "provider_evses",
                column: "evse_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_evses_provider_charger_id_external_id",
                schema: "charging",
                table: "provider_evses",
                columns: new[] { "provider_charger_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_stations_charging_provider_id_external_id",
                schema: "charging",
                table: "provider_stations",
                columns: new[] { "charging_provider_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_stations_station_id",
                schema: "charging",
                table: "provider_stations",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_connector_id_starts_at",
                schema: "charging",
                table: "reservations",
                columns: new[] { "connector_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reservations_driver_id",
                schema: "charging",
                table: "reservations",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_public_id",
                schema: "charging",
                table: "reservations",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_charging_provider_id_provider_session_id",
                schema: "charging",
                table: "sessions",
                columns: new[] { "charging_provider_id", "provider_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_connector_id",
                schema: "charging",
                table: "sessions",
                column: "connector_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_reservation_id",
                schema: "charging",
                table: "sessions",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_stations_public_id",
                schema: "charging",
                table: "stations",
                column: "public_id",
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE charging.reservations
                ADD CONSTRAINT ex_reservations_connector_slot
                EXCLUDE USING gist (connector_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
                WHERE (state IN (0, 1));
                """);
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON TABLE charging.audit_entries FROM platform_writer;");
            migrationBuilder.Sql(
                "REVOKE ALL ON TABLE charging.ef_migrations_history FROM platform_writer, platform_reader;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempts",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "deadlines",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "file_metadata",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "operator_assignments",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "provider_connectors",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "operations",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "operator_users",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "provider_evses",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "provider_chargers",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "reservations",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "provider_stations",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "connectors",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "drivers",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "charging_providers",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "evses",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "chargers",
                schema: "charging");

            migrationBuilder.DropTable(
                name: "stations",
                schema: "charging");
        }
    }
}
