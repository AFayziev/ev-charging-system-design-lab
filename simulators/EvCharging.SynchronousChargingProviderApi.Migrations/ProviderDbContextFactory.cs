using EvCharging.SynchronousChargingProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EvCharging.SynchronousChargingProviderApi.Migrations;

public sealed class ProviderDbContextFactory : IDesignTimeDbContextFactory<ProviderDbContext>
{
    public ProviderDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__synchronous-provider");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("ConnectionStrings__synchronous-provider is required.");
        }

        var options = new DbContextOptionsBuilder<ProviderDbContext>().UseNpgsql(connection);
        options.UseProviderModel();
        return new ProviderDbContext(options.Options);
    }
}
