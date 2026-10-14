using EvCharging.Testing;
using Npgsql;
using Xunit;

namespace EvCharging.Persistence.Tests;

public sealed class DatabaseAccessTests(PlatformDatabaseFixture database)
    : IClassFixture<PlatformDatabaseFixture>
{
    [Theory]
    [InlineData("INSERT INTO charging.charging_providers (name) VALUES ('denied')")]
    [InlineData("UPDATE charging.charging_providers SET name = 'denied'")]
    [InlineData("DELETE FROM charging.charging_providers")]
    public Task ReadRoute_Mutation_Denied(string sql) => AssertDeniedAsync("platform_reader", sql);

    [Theory]
    [InlineData("UPDATE charging.audit_entries SET details_version = 2")]
    [InlineData("DELETE FROM charging.audit_entries")]
    public Task AuditHistory_RuntimeMutation_Denied(string sql) => AssertDeniedAsync("platform_writer", sql);

    [Theory]
    [InlineData("platform_writer", "gateway_auth")]
    [InlineData("platform_reader", "gateway_auth")]
    [InlineData("platform_migrator", "gateway_auth")]
    [InlineData("gateway_auth_runtime", "charging")]
    [InlineData("gateway_auth_migrator", "charging")]
    [InlineData("notification_messaging", "gateway_auth")]
    public Task SchemaAccess_OtherOwner_Denied(string role, string schema) =>
        AssertDeniedAsync(role, $"SELECT id FROM {schema}.sessions");

    [Theory]
    [InlineData("platform_writer", "charging")]
    [InlineData("platform_reader", "charging")]
    [InlineData("gateway_auth_runtime", "gateway_auth")]
    [InlineData("platform_writer", "public")]
    [InlineData("platform_reader", "public")]
    [InlineData("gateway_auth_runtime", "public")]
    [InlineData("notification_messaging", "charging")]
    [InlineData("notification_messaging", "notification_messages")]
    public Task RuntimeRole_CreateTable_Denied(string role, string schema) =>
        AssertDeniedAsync(role, $"CREATE TABLE {schema}.unauthorized (id bigint)");

    [Theory]
    [InlineData("platform_writer", "charging")]
    [InlineData("platform_reader", "charging")]
    [InlineData("gateway_auth_runtime", "gateway_auth")]
    [InlineData("notification_messaging", "charging")]
    public Task RuntimeRole_MigrationHistory_Denied(string role, string schema) =>
        AssertDeniedAsync(role, $"SELECT * FROM {schema}.ef_migrations_history");

    [Theory]
    [InlineData("UPDATE charging.reservations SET state = state")]
    [InlineData("UPDATE charging.drivers SET email = email")]
    [InlineData("UPDATE charging.notifications SET recipient_email = recipient_email")]
    [InlineData("DELETE FROM charging.notifications")]
    [InlineData("DELETE FROM charging.notification_delivery_attempts")]
    [InlineData("UPDATE charging.notification_delivery_attempts SET notification_id = notification_id")]
    [InlineData("UPDATE charging.reservation_notifications SET kind = kind")]
    [InlineData("DELETE FROM charging.reservation_notifications")]
    [InlineData("SELECT * FROM api_messages.wolverine_incoming_envelopes")]
    [InlineData("SELECT * FROM charging_messages.wolverine_incoming_envelopes")]
    public Task NotificationRole_OtherEffects_Denied(string sql) => AssertDeniedAsync("notification_messaging", sql);

    [Theory]
    [InlineData("platform_writer", "SELECT * FROM charging.notifications")]
    [InlineData("platform_writer", "INSERT INTO charging.notifications DEFAULT VALUES")]
    [InlineData("platform_writer", "UPDATE charging.notifications SET body = body")]
    [InlineData("platform_writer", "DELETE FROM charging.notifications")]
    [InlineData("platform_writer", "TRUNCATE charging.notifications")]
    [InlineData("platform_reader", "SELECT recipient_email, subject, body, html_body FROM charging.notifications")]
    [InlineData("platform_writer", "SELECT * FROM charging.reservation_notifications")]
    [InlineData("platform_writer", "INSERT INTO charging.reservation_notifications DEFAULT VALUES")]
    [InlineData("platform_writer", "UPDATE charging.reservation_notifications SET kind = kind")]
    [InlineData("platform_writer", "DELETE FROM charging.reservation_notifications")]
    [InlineData("platform_writer", "TRUNCATE charging.reservation_notifications")]
    [InlineData("platform_reader", "SELECT * FROM charging.reservation_notifications")]
    [InlineData("platform_writer", "SELECT * FROM charging.notification_delivery_attempts")]
    [InlineData("platform_reader", "SELECT * FROM charging.notification_delivery_attempts")]
    public async Task NotificationHistory_ApiAccess_DeniedAfterMigrationAndReprovisioning(string role, string sql)
    {
        await AssertDeniedAsync(role, sql);

        await database.ReprovisionAsync();

        await AssertDeniedAsync(role, sql);
    }

    private async Task AssertDeniedAsync(string role, string sql)
    {
        await using var connection = database.CreateConnection(role);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
