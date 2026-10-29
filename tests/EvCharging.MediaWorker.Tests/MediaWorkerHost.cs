using System.Collections.Concurrent;
using EvCharging.Contracts.Events.Stations;
using EvCharging.Contracts.Media;
using EvCharging.MediaWorker.Configuration;
using EvCharging.MediaWorker.Handlers;
using EvCharging.MediaWorker.Persistence;
using EvCharging.MediaWorker.Processing;
using EvCharging.Persistence;
using EvCharging.Persistence.Auditing;
using EvCharging.Testing;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace EvCharging.MediaWorker.Tests;

// The media worker's handlers, failure policies, and processor over durable local queues, with the database access
// of its own role and the storage access of its own user.
internal sealed class MediaWorkerHost : IAsyncDisposable
{
    public const int ThumbnailEdge = 48;
    public const int DisplayEdge = 160;
    public const long MaximumPhotoPixels = 200_000;
    private readonly IHost _host;

    private MediaWorkerHost(IHost host) => _host = host;

    public IMessageBus Bus => _host.Services.GetRequiredService<IMessageBus>();

    public static IReadOnlyCollection<Guid> StationChangesSnapshot => [.. StationChangeCapture.Stations];

    // Tests inject database failures by passing further interceptors.
    public static async Task<MediaWorkerHost> StartAsync(
        PlatformDatabaseFixture database, ObjectStoreFixture storage, params IInterceptor[] interceptors)
    {
        TimeSpan[] retryDelays = [TimeSpan.FromMilliseconds(200)];
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<IAuditActorAccessor, WorkerAuditActorAccessor>();
        builder.Services.AddScoped<AuditEntryWriter>();
        builder.Services.AddScoped<AuditSaveChangesInterceptor>();
        builder.Services.AddDbContext<PlatformDbContext>((services, options) =>
            options.UseNpgsql(database.GetDataSource("media_messaging"))
                .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>())
                .AddInterceptors(interceptors));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(storage.MediaWorkerStore);
        builder.Services.AddSingleton(Options.Create(new MediaProcessingOptions
        {
            ThumbnailEdge = ThumbnailEdge,
            DisplayEdge = DisplayEdge,
            WebpQuality = 80,
            MaximumConcurrentProcessing = 2,
            MissingUploadRetryDelay = TimeSpan.FromMilliseconds(200),
            RetryDelays = retryDelays
        }));
        builder.Services.AddSingleton(Options.Create(new StationMediaLimitsOptions
        {
            MaximumPhotoPixels = MaximumPhotoPixels
        }));
        builder.Services.AddSingleton<StationPhotoProcessor>();
        builder.UseWolverine(options =>
        {
            options.UseEntityFrameworkCoreTransactions();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(ProcessStationMediaMessageHandler));
            options.Discovery.IncludeType(typeof(CheckStationMediaUploadMessageHandler));
            options.Discovery.IncludeType(typeof(DeleteStationMediaObjectsMessageHandler));
            options.Discovery.IncludeType(typeof(StationChangeCapture));
            options.PersistMessagesWithPostgresql(database.GetConnectionString("platform_migrator"), "media_messages");
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.Policies.UseDurableLocalQueues();
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
            options.AddMediaFailurePolicies(retryDelays);
            options.PublishMessage<ProcessStationMediaMessage>().ToLocalQueue("station-media-processing-test");
            options.PublishMessage<CheckStationMediaUploadMessage>().ToLocalQueue("station-media-check-test");
            options.PublishMessage<DeleteStationMediaObjectsMessage>().ToLocalQueue("station-media-delete-test");
            options.PublishMessage<StationChangedEvent>().ToLocalQueue("station-changed-test");
        });
        var host = builder.Build();
        await host.StartAsync();
        return new MediaWorkerHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

public static class StationChangeCapture
{
    internal static readonly ConcurrentQueue<Guid> Stations = [];

    public static void Handle(StationChangedEvent message) => Stations.Enqueue(message.StationId);
}
