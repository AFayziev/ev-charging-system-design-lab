using JasperFx;
using Microsoft.Extensions.Configuration;
using Wolverine;
using Wolverine.Postgresql;

namespace EvCharging.Persistence.Extensions;

public static class MessageStorageExtensions
{
    public static WolverineOptions AddMessageStorage(
        this WolverineOptions options, IConfiguration configuration, string serviceName)
    {
        var connection = configuration.GetConnectionString("messaging");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("The messaging connection is required.");
        }

        options.ServiceName = serviceName;
        options.PersistMessagesWithPostgresql(connection, $"{serviceName}_messages");
        options.AutoBuildMessageStorageOnStartup = AutoCreate.None;

        return options;
    }
}
