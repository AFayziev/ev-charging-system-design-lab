using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EvCharging.Gateway.Persistence;

public sealed class AuthenticationDbContextFactory : IDesignTimeDbContextFactory<AuthenticationDbContext>
{
    public AuthenticationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__gateway-auth-migration-db");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("The ConnectionStrings__gateway-auth-migration-db environment variable is required.");
        }

        return new AuthenticationDbContext(new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "gateway_auth")).Options);
    }
}
