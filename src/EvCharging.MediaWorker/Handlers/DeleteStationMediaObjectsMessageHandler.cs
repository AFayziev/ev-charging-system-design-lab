using EvCharging.Contracts.Media;
using EvCharging.S3ObjectStore;

namespace EvCharging.MediaWorker.Handlers;

public sealed class DeleteStationMediaObjectsMessageHandler(ObjectStore objectStore)
{
    public async Task HandleAsync(DeleteStationMediaObjectsMessage message, CancellationToken cancellationToken)
    {
        await objectStore.DeletePublicPrefixAsync(
            StationMediaObjectKeys.VariantPrefix(message.StationPublicId, message.ItemPublicId), cancellationToken);
        await objectStore.DeletePrivateAsync(
            StationMediaObjectKeys.Upload(message.StationPublicId, message.ItemPublicId), cancellationToken);
    }
}
