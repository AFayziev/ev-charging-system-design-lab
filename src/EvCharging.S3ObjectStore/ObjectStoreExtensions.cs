using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EvCharging.S3ObjectStore;

public static class ObjectStoreExtensions
{
    public static IHostApplicationBuilder AddObjectStore(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<ObjectStoreOptions>()
            .BindConfiguration(ObjectStoreOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ObjectStoreClients>();
        builder.Services.AddSingleton<ObjectStore>();
        return builder;
    }
}
