using EvCharging.Contracts.Media;
using EvCharging.Domain.Media;
using EvCharging.Persistence;
using EvCharging.S3ObjectStore;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.MediaWorker.Handlers;

public sealed class CheckStationMediaUploadMessageHandler(
    PlatformDbContext database, IDbContextOutbox outbox, ObjectStore objectStore)
{
    // Runs at the upload deadline for a photo whose completion notice never arrived: a file that did arrive is
    // processed, and otherwise the photo fails and no longer counts toward the photo limit.
    [NonTransactional]
    public async Task HandleAsync(CheckStationMediaUploadMessage message, CancellationToken cancellationToken)
    {
        var item = await database.StationMediaItems
            .Include(item => item.Station)
            .SingleOrDefaultAsync(item => item.Id == message.ItemId, cancellationToken);
        if (item is not { State: StationMediaState.Uploading })
        {
            return;
        }

        var upload = await objectStore.GetPrivateObjectAsync(
            StationMediaObjectKeys.Upload(item.Station!.PublicId, item.PublicId), cancellationToken);
        outbox.Enroll(database);
        if (item.ResolveExpiredUpload(upload is not null))
        {
            await outbox.PublishAsync(new ProcessStationMediaMessage(item.Id));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }
}
