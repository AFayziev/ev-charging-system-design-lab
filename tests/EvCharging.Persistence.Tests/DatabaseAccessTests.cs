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

    [Fact]
    public async Task AuditHistory_Reprovisioned_ReaderAccessAndAppendOnlyPrivilegesPreserved()
    {
        await database.ReprovisionAsync();
        await AssertDeniedAsync("platform_writer", "UPDATE charging.audit_entries SET details_version = 2");
        await AssertDeniedAsync("platform_writer", "DELETE FROM charging.audit_entries");
        await using var reader = database.CreateConnection("platform_reader");
        await reader.OpenAsync();
        await using var read = new NpgsqlCommand("SELECT COUNT(*) FROM charging.audit_entries", reader);
        Assert.IsType<long>(await read.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("platform_writer", "gateway_auth")]
    [InlineData("platform_reader", "gateway_auth")]
    [InlineData("platform_migrator", "gateway_auth")]
    [InlineData("gateway_auth_runtime", "charging")]
    [InlineData("gateway_auth_migrator", "charging")]
    public Task SchemaAccess_OtherOwner_Denied(string role, string schema) =>
        AssertDeniedAsync(role, $"SELECT id FROM {schema}.sessions");

    [Theory]
    [InlineData("platform_writer", "charging")]
    [InlineData("platform_reader", "charging")]
    [InlineData("gateway_auth_runtime", "gateway_auth")]
    [InlineData("platform_writer", "public")]
    [InlineData("platform_reader", "public")]
    [InlineData("gateway_auth_runtime", "public")]
    public Task RuntimeRole_CreateTable_Denied(string role, string schema) =>
        AssertDeniedAsync(role, $"CREATE TABLE {schema}.unauthorized (id bigint)");

    [Theory]
    [InlineData("platform_writer", "charging")]
    [InlineData("platform_reader", "charging")]
    [InlineData("gateway_auth_runtime", "gateway_auth")]
    public Task RuntimeRole_MigrationHistory_Denied(string role, string schema) =>
        AssertDeniedAsync(role, $"SELECT * FROM {schema}.ef_migrations_history");

    [Fact]
    public async Task WriteRoute_GrantedMutations_VisibleToReader()
    {
        await using var writer = database.CreateConnection("platform_writer");
        await writer.OpenAsync();
        await using var insert = new NpgsqlCommand(
            "INSERT INTO charging.charging_providers (name) VALUES ('initial') RETURNING id", writer);
        var id = Assert.IsType<long>(await insert.ExecuteScalarAsync());
        await using var update = new NpgsqlCommand(
            "UPDATE charging.charging_providers SET name = 'updated' WHERE id = $1", writer);
        update.Parameters.AddWithValue(id);
        Assert.Equal(1, await update.ExecuteNonQueryAsync());

        await using var reader = database.CreateConnection("platform_reader");
        await reader.OpenAsync();
        await using var read = new NpgsqlCommand("SELECT name FROM charging.charging_providers WHERE id = $1", reader);
        read.Parameters.AddWithValue(id);
        Assert.Equal("updated", await read.ExecuteScalarAsync());

        await using var delete = new NpgsqlCommand("DELETE FROM charging.charging_providers WHERE id = $1", writer);
        delete.Parameters.AddWithValue(id);
        Assert.Equal(1, await delete.ExecuteNonQueryAsync());
        Assert.Null(await read.ExecuteScalarAsync());
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
