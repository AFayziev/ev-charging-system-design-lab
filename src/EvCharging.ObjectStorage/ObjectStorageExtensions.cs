using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EvCharging.ObjectStorage;

public static class ObjectStorageExtensions
{
    public static IHostApplicationBuilder AddObjectStorage(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<ObjectStorageOptions>()
            .BindConfiguration(ObjectStorageOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ObjectStorageClients>();
        builder.Services.AddSingleton<ObjectStore>();
        return builder;
    }
}
