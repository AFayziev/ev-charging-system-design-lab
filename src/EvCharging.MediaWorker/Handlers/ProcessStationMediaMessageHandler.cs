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
    StationPhotoProcessor photoProcessor,
    StationVideoVerifier videoVerifier,
    TimeProvider clock,
    IOptions<MediaProcessingOptions> processing,
    ILogger<ProcessStationMediaMessageHandler> logger)
{
    // Storage and decoding run with no transaction open. Object keys are fixed per item and written only if
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

        var processed = await PrepareAsync(item, cancellationToken);

        // Releases the streams, and with them the video's temporary file, once the item has settled.
        using var prepared = processed.IsSuccess ? processed.Value : null;
        if (processed.HasError<StationUploadNotArrivedError>())
        {
            outgoing.Delay(message, processing.Value.MissingUploadRetryDelay);
            return outgoing;
        }

        outbox.Enroll(database);
        var settled = processed.HasError<StationMediaRejectedError>(out var rejections)
            ? Reject(item, rejections.First().FailureCode)
            : await PublishAsync(item, replaced, prepared!, cancellationToken);
        if (settled.IsFailed)
        {
            // The item was Processing when loaded, so settling it cannot be rejected. This is a defect, and the
            // message goes to the error queue where it stays visible.
            throw new InvalidOperationException(
                $"Media item {item.PublicId} could not be settled: "
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

    private async Task<Result<PreparedStationMedia>> PrepareAsync(
        StationMediaItem item, CancellationToken cancellationToken)
    {
        var station = item.Station!;
        try
        {
            var prepared = item.Kind == StationMediaKind.Video
                ? await PrepareVideoAsync(item, cancellationToken)
                : await PreparePhotoAsync(item, cancellationToken);
            if (prepared is not null)
            {
                return prepared;
            }

            return clock.GetUtcNow() < item.UploadExpiresAt
                ? Result.Fail<PreparedStationMedia>(new StationUploadNotArrivedError())
                : Reject(StationMediaFailure.UploadMissing);
        }
        catch (InvalidDataException exception)
        {
            // Storage only accepts the signed length, so a longer object is not the file this upload declared.
            logger.LogWarning(
                exception,
                "Upload of {Kind} {ItemId} of station {StationId} is longer than the declared {ByteLength} bytes",
                item.Kind,
                item.PublicId,
                station.PublicId,
                item.ByteLength);
            return Reject(StationMediaFailure.Undecodable);
        }
    }

    // The upload is an untrusted file, so a decoder failure means the file is not a usable photo. Null means that
    // storage has no upload.
    private async Task<Result<PreparedStationMedia>?> PreparePhotoAsync(
        StationMediaItem item, CancellationToken cancellationToken)
    {
        var station = item.Station!;
        var upload = await objectStore.ReadPrivateAsync(
            StationMediaObjectKeys.Upload(station.PublicId, item.PublicId), item.ByteLength, cancellationToken);
        if (upload is null)
        {
            return null;
        }

        try
        {
            var variants = photoProcessor.Process(upload, item.ContentType);
            return variants.IsFailed
                ? variants.ToResult<PreparedStationMedia>()
                : Result.Ok(new PreparedStationMedia(
                    variants.Value.Select(variant => new PreparedStationObject(
                        StationMediaObjectKeys.Variant(station.PublicId, item.PublicId, variant.Name),
                        new MemoryStream(variant.Content),
                        "image/webp"))
                        .ToList()));
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            logger.LogWarning(exception, "Photo {ItemId} could not be decoded", item.PublicId);
            return Reject(StationMediaFailure.Undecodable);
        }
    }

    // A video is verified from its header boxes and published as uploaded. It is held in a temporary file rather
    // than in memory, which is deleted when the prepared media is disposed. Null means that storage has no upload.
    private async Task<Result<PreparedStationMedia>?> PrepareVideoAsync(
        StationMediaItem item, CancellationToken cancellationToken)
    {
        var station = item.Station!;
        var file = new FileStream(
            Path.Combine(Path.GetTempPath(), $"station-video-{Guid.NewGuid():N}"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.DeleteOnClose);
        var handedOver = false;
        try
        {
            if (!await objectStore.DownloadPrivateAsync(
                StationMediaObjectKeys.Upload(station.PublicId, item.PublicId),
                item.ByteLength,
                file,
                cancellationToken))
            {
                return null;
            }

            var verified = Verify(file, item);
            if (verified.IsFailed)
            {
                return verified.ToResult<PreparedStationMedia>();
            }

            file.Position = 0;
            var video = new PreparedStationObject(
                StationMediaObjectKeys.Video(station.PublicId, item.PublicId), file, "video/mp4");
            handedOver = true;
            return Result.Ok(new PreparedStationMedia([video]));
        }
        finally
        {
            // Once handed over, disposing the prepared media releases the file.
            if (!handedOver)
            {
                file.Dispose();
            }
        }
    }

    // The upload is an untrusted file, so a failure to read it as a video means it is not a usable one. A fault of
    // the temporary file is the worker's own and is left to retry.
    private Result Verify(FileStream file, StationMediaItem item)
    {
        try
        {
            return videoVerifier.Verify(file, item.ContentType);
        }
        catch (Exception exception) when (
            exception is not (OperationCanceledException or OutOfMemoryException or IOException))
        {
            logger.LogWarning(exception, "Video {ItemId} could not be read", item.PublicId);
            return Result.Fail(new StationMediaRejectedError(StationMediaFailure.Undecodable));
        }
    }

    private static Result<PreparedStationMedia> Reject(string failureCode) =>
        Result.Fail<PreparedStationMedia>(new StationMediaRejectedError(failureCode));

    private Result Reject(StationMediaItem item, string failureCode)
    {
        logger.LogInformation(
            "{Kind} {ItemId} of station {StationId} was rejected: {FailureCode}",
            item.Kind,
            item.PublicId,
            item.Station!.PublicId,
            failureCode);
        return item.Fail(failureCode);
    }

    private async Task<Result> PublishAsync(
        StationMediaItem item,
        StationMediaItem? replaced,
        PreparedStationMedia prepared,
        CancellationToken cancellationToken)
    {
        var station = item.Station!;
        await Task.WhenAll(prepared.Objects.Select(preparedObject => objectStore.PutPublicAsync(
            preparedObject.Key, preparedObject.Content, preparedObject.ContentType, cancellationToken)));

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
