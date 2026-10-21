using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace EvCharging.DocumentStore;

public static class DocumentStoreExtensions
{
    /// <summary>The Aspire connection name, which is also the database name.</summary>
    public const string ConnectionName = "observations";

    public static IHostApplicationBuilder AddDocumentStore(this IHostApplicationBuilder builder)
    {
        builder.AddMongoDBClient(ConnectionName);
        builder.Services.AddSingleton(provider =>
            new ObservationStore(provider.GetRequiredService<IMongoClient>().GetDatabase(ConnectionName)));
        return builder;
    }
}
