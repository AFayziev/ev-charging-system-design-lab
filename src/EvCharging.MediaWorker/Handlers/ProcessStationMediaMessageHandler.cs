using EvCharging.Contracts.Media;
using EvCharging.Domain.Media;
using EvCharging.MediaWorker.Configuration;
using EvCharging.MediaWorker.Processing;
using EvCharging.Messaging;
using EvCharging.Persistence;
using EvCharging.S3ObjectStore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.MediaWorker.Handlers;

public sealed class ProcessStationMediaMessageHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    ObjectStore objectStore,
    StationPhotoProcessor processor,
    TimeProvider clock,
    IOptions<MediaProcessingOptions> processing,
    ILogger<ProcessStationMediaMessageHandler> logger)
{
    // Storage and decoding run with no transaction open. Variant keys are fixed per photo and written only if
    // absent, so a retry after a lost commit finds them in place and publishes them in one transaction.
    [NonTransactional]
    public async Task<OutgoingMessages> HandleAsync(
        ProcessStationMediaMessage message, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var item = await database.StationMediaItems
            .Include(item => item.Station)
            .SingleOrDefaultAsync(item => item.Id == message.ItemId, cancellationToken);
        if (item is not { State: StationMediaState.Processing })
        {
            // Removed, or settled by an earlier delivery.
            return outgoing;
        }

        var station = item.Station!;
        StationMediaItem? replaced = null;
        if (item.ReplacesItemId is { } replacedId)
        {
            replaced = await database.StationMediaItems.SingleOrDefaultAsync(
                item => item.Id == replacedId, cancellationToken);
            if (replaced is null)
            {
                // The photo it replaces was removed, and this one went with it; the removal cleans up the files.
                return outgoing;
            }
        }

        var processed = await ProcessUploadAsync(item, cancellationToken);
        if (processed.HasError<StationUploadNotArrivedError>())
        {
            outgoing.Delay(message, processing.Value.MissingUploadRetryDelay);
            return outgoing;
        }

        outbox.Enroll(database);
        var settled = processed.HasError<StationPhotoRejectedError>(out var rejections)
            ? Reject(item, rejections.First().FailureCode)
            : await PublishAsync(item, replaced, processed.Value, cancellationToken);
        if (settled.IsFailed)
        {
            // The photo was Processing when loaded, so settling it cannot be rejected. This is a defect, and the
            // message goes to the error queue where it stays visible.
            throw new InvalidOperationException(
                $"Photo {item.PublicId} could not be settled: "
                + string.Join(' ', settled.Errors.Select(error => error.Message)));
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            if (await database.StationMediaItems.AnyAsync(item => item.Id == message.ItemId, cancellationToken))
            {
                // A gallery change touched a photo involved in this publication; the retry reads the new state.
                throw;
            }

            // The photo was removed meanwhile. Its removal may have cleaned up before the variants were written,
            // so they are cleaned up here too.
            await objectStore.DeletePublicPrefixAsync(
                StationMediaObjectKeys.VariantPrefix(station.PublicId, item.PublicId), cancellationToken);
        }

        return outgoing;
    }

    private async Task<Result<IReadOnlyList<StationPhotoVariant>>> ProcessUploadAsync(
        StationMediaItem item, CancellationToken cancellationToken)
    {
        var station = item.Station!;
        try
        {
            var upload = await objectStore.ReadPrivateAsync(
                StationMediaObjectKeys.Upload(station.PublicId, item.PublicId), item.ByteLength, cancellationToken);
            if (upload is not null)
            {
                return ProcessUpload(item, upload);
            }

            return clock.GetUtcNow() < item.UploadExpiresAt
                ? Result.Fail<IReadOnlyList<StationPhotoVariant>>(new StationUploadNotArrivedError())
                : StationPhotoProcessor.Reject(StationMediaFailure.UploadMissing);
        }
        catch (InvalidDataException exception)
        {
            // Storage only accepts the signed length, so a longer object is not the file this upload declared.
            logger.LogWarning(
                exception,
                "Upload of photo {ItemId} of station {StationId} is longer than the declared {ByteLength} bytes",
                item.PublicId,
                station.PublicId,
                item.ByteLength);
            return StationPhotoProcessor.Reject(StationMediaFailure.Undecodable);
        }
    }

    // The upload is an untrusted file, so a decoder failure means the file is not a usable photo.
    private Result<IReadOnlyList<StationPhotoVariant>> ProcessUpload(StationMediaItem item, byte[] upload)
    {
        try
        {
            return processor.Process(upload, item.ContentType);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            logger.LogWarning(exception, "Photo {ItemId} could not be decoded", item.PublicId);
            return StationPhotoProcessor.Reject(StationMediaFailure.Undecodable);
        }
    }

    private Result Reject(StationMediaItem item, string failureCode)
    {
        logger.LogInformation(
            "Photo {ItemId} of station {StationId} was rejected: {FailureCode}",
            item.PublicId,
            item.Station!.PublicId,
            failureCode);
        return item.Fail(failureCode);
    }

    private async Task<Result> PublishAsync(
        StationMediaItem item,
        StationMediaItem? replaced,
        IReadOnlyList<StationPhotoVariant> variants,
        CancellationToken cancellationToken)
    {
        var station = item.Station!;
        await Task.WhenAll(variants.Select(async variant =>
        {
            using var content = new MemoryStream(variant.Content);
            await objectStore.PutPublicAsync(
                StationMediaObjectKeys.Variant(station.PublicId, item.PublicId, variant.Name),
                content,
                "image/webp",
                cancellationToken);
        }));

        var published = item.Publish(replaced, clock.GetUtcNow());
        if (published.IsFailed)
        {
            return published;
        }

        if (replaced is not null)
        {
            database.StationMediaItems.Remove(replaced);
            await outbox.PublishAsync(new DeleteStationMediaObjectsMessage(station.PublicId, replaced.PublicId));
        }

        await outbox.PublishStationChangedAsync(station.Id);
        return Result.Ok();
    }
}
