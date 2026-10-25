using EvCharging.Persistence;
using EvCharging.SearchIndexWorker.Indexing;
using Wolverine;

namespace EvCharging.SearchIndexWorker.Handlers;

public static class EnsureStationSearchIndexCommandHandler
{
    // The command is idempotent: repeats, redeliveries, and requests from several instances find nothing to do.
    public static Task HandleAsync(
        EnsureStationSearchIndexCommand message,
        Envelope envelope,
        PlatformDbContext database,
        StationSearchIndexBuilder builder,
        CancellationToken cancellationToken) =>
        builder.EnsureAsync(database, envelope.Id, cancellationToken);
}
