using EvCharging.Contracts.Events.Stations;
using EvCharging.Persistence;
using EvCharging.SearchIndexWorker.Indexing;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.SearchIndexWorker.Handlers;

public static class StationChangedEventHandler
{
    // Events for one station run one at a time, and each reads the committed row, so the last write is the newest.
    // With no index at all the edit is not lost: the rebuild requested here reads the committed row.
    public static async Task<EnsureStationSearchIndexCommand?> HandleAsync(
        StationChangedEvent message,
        PlatformDbContext database,
        StationSearchIndexWriter writer,
        CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .AsNoTracking()
            .Where(station => station.Id == message.StationId)
            .ToSources()
            .SingleAsync(cancellationToken);

        var indexed = await writer.IndexAsync(StationSearchProjection.ToDocument(station), cancellationToken);
        return indexed ? null : new EnsureStationSearchIndexCommand();
    }
}
