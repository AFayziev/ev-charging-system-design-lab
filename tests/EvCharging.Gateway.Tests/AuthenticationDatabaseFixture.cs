using EvCharging.Gateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EvCharging.Gateway.Tests;

public sealed class AuthenticationDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.11-bookworm").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE ROLE gateway_auth_runtime", connection);
        await command.ExecuteNonQueryAsync();
        await using var database = new AuthenticationDbContext(new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseNpgsql(ConnectionString).Options);
        await database.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}
