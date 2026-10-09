using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EvCharging.Persistence.Migrations;

public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__platform-migration");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                "The ConnectionStrings__platform-migration environment variable is required.");
        }

        return new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection).Options);
    }
}
