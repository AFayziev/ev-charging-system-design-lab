using EvCharging.Gateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EvCharging.Persistence.Tests;

public sealed class PlatformDatabaseFixture : IAsyncLifetime
{
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly PostgreSqlContainer _postgres;

    public PlatformDatabaseFixture()
    {
        _postgres = new PostgreSqlBuilder("postgres:18.6-bookworm")
            .WithDatabase("evcharging")
            .WithUsername("postgres")
            .WithPassword(_password)
            .WithEnvironment("GATEWAY_AUTH_PASSWORD", _password)
            .WithEnvironment("GATEWAY_AUTH_MIGRATION_PASSWORD", _password)
            .WithEnvironment("PLATFORM_WRITE_PASSWORD", _password)
            .WithEnvironment("PLATFORM_READ_PASSWORD", _password)
            .WithEnvironment("PLATFORM_MIGRATION_PASSWORD", _password)
            .WithResourceMapping(Path.Combine(AppContext.BaseDirectory, "provisioning"), "/provisioning/")
            .Build();
    }

    public NpgsqlConnection CreateConnection(string role)
    {
        var options = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = role,
            Password = _password
        };

        return new(options.ConnectionString);
    }

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _postgres.StartAsync(timeout.Token);

        await ReprovisionAsync(timeout.Token);

        await using var authenticationConnection = CreateConnection("gateway_auth_migrator");
        await using var authentication = new AuthenticationDbContext(
            new DbContextOptionsBuilder<AuthenticationDbContext>().UseNpgsql(authenticationConnection).Options);
        await authentication.Database.MigrateAsync(timeout.Token);

        await using var platformConnection = CreateConnection("platform_migrator");
        await using var platform = new PlatformDbContext(
            new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnection).Options);
        await platform.Database.MigrateAsync(timeout.Token);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public async Task ReprovisionAsync(CancellationToken cancellationToken = default)
    {
        var result = await _postgres.ExecAsync(
            ["psql", "-X", "-q", "-U", "postgres", "-d", "evcharging", "-f", "/provisioning/bootstrap.sql"],
            cancellationToken);
        Assert.True(result.ExitCode == 0, result.Stderr);
    }
}
