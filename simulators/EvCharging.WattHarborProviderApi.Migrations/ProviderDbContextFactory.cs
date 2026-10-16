using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EvCharging.WattHarborProviderApi.Migrations;

public sealed class ProviderDbContextFactory : IDesignTimeDbContextFactory<ProviderDbContext>
{
    public ProviderDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__wattharbor");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("ConnectionStrings__wattharbor is required.");
        }

        var options = new DbContextOptionsBuilder<ProviderDbContext>().UseNpgsql(connection);
        options.UseProviderModel();
        return new ProviderDbContext(options.Options);
    }
}
