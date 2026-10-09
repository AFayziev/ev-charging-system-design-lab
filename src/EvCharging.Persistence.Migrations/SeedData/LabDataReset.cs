using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Respawn;
using Respawn.Graph;

namespace EvCharging.Persistence.Migrations.SeedData;

public static class LabDataReset
{
    public static async Task ResetAsync(PlatformDbContext context, CancellationToken cancellationToken)
    {
        var options = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        var schema = context.Model.GetDefaultSchema()!;
        var historyTable = new Table(
            options.MigrationsHistoryTableSchema ?? schema,
            options.MigrationsHistoryTableName ?? HistoryRepository.DefaultTableName);
        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var connection = context.Database.GetDbConnection();
            var reset = await Respawner.CreateAsync(
                connection,
                new()
                {
                    DbAdapter = DbAdapter.Postgres,
                    SchemasToInclude = [schema],
                    TablesToIgnore = [historyTable],
                    CommandTimeout = context.Database.GetCommandTimeout()
                });
            cancellationToken.ThrowIfCancellationRequested();
            await reset.ResetAsync(connection);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
