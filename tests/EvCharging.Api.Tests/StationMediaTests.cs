using System.Collections.Concurrent;
using System.Net;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.StationMedia;
using EvCharging.Api.Features.Stations;
using EvCharging.Contracts.Events.Stations;
using EvCharging.Contracts.Media;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Media;
using EvCharging.Persistence.Auditing;
using EvCharging.S3ObjectStore;
using EvCharging.Testing;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Wolverine;
using Wolverine.Tracking;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class StationMediaTests(PlatformDatabaseFixture database, ObjectStoreFixture objectStore) :
    IClassFixture<PlatformDatabaseFixture>,
    IClassFixture<ObjectStoreFixture>,
    IAsyncLifetime
{
    private const string Jpeg = "image/jpeg";
    private const int MaximumPhotos = 2;
    private static readonly byte[] Photo = "synthetic photo bytes"u8.ToArray();
    private static readonly ConcurrentQueue<Guid> ProcessingRequests = [];
    private static readonly ConcurrentQueue<Guid> DeletionRequests = [];
    private static readonly ConcurrentQueue<Guid> StationChanges = [];
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private ApiHandlerHost? _api;

    public async Task InitializeAsync() =>
        _api = await ApiHandlerHost.StartAsync<StartStationPhotoUploadCommandHandler>(
            database,
            _clock,
            services =>
            {
                services.AddAuthorization();
                services.AddScoped<IAuthorizationHandler, StationAssignmentAuthorizationHandler>();
                services.AddSingleton(objectStore.ApiStore);
                services.Configure<StationMediaOptions>(options =>
                {
                    options.MaximumPhotosPerStation = MaximumPhotos;
                    options.MaximumPhotoBytes = 1000;
                    options.MaximumPhotoPixels = 1_000_000;
                    options.PhotoContentTypes = [Jpeg];
                    options.UploadUrlLifetime = TimeSpan.FromMinutes(30);
                });
            },
            options =>
            {
                options.Policies.UseDurableLocalQueues();
                options.Discovery.IncludeType<CompleteStationMediaUploadCommandHandler>();
                options.Discovery.IncludeType<GetStationGalleryEditorQueryHandler>();
                options.Discovery.IncludeType<ReorderStationGalleryCommandHandler>();
                options.Discovery.IncludeType<RemoveStationMediaCommandHandler>();
                options.Discovery.IncludeType(typeof(MessageCapture));
                options.PublishMessage<ProcessStationMediaMessage>().ToLocalQueue("station-media-processing-test");
                options.PublishMessage<CheckStationMediaUploadMessage>().ToLocalQueue("station-media-check-test");
                options.PublishMessage<DeleteStationMediaObjectsMessage>().ToLocalQueue("station-media-delete-test");
                options.PublishMessage<StationChangedEvent>().ToLocalQueue("station-changed-test");
            });

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
    }

    [Fact]
    public async Task StartPhotoUpload_AssignedOperator_SignsAnUploadAndRecordsTheGalleryChange()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();

        var result = await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion);

        Assert.True(result.IsSuccess);
        var upload = result.Value;
        Assert.Equal(station.GalleryVersion + 1, upload.GalleryVersion);
        Assert.Equal(HttpStatusCode.OK, (await objectStore.PutAsync(upload.UploadUrl, Photo, Jpeg)).StatusCode);
        await using var context = database.CreateContext();
        var item = await context.StationMediaItems.SingleAsync(item => item.PublicId == upload.ItemId);
        Assert.Equal((station.Id, 0, StationMediaState.Uploading), (item.StationId, item.Position, item.State));
        Assert.True(await context.AuditEntries.AnyAsync(audit =>
            audit.ResourceType == AuditResourceType.StationMediaItem && audit.ResourceId == item.Id
            && audit.Action == AuditAction.Added));
    }

    [Fact]
    public async Task StartPhotoUpload_OperatorOfAnotherStation_IsForbidden()
    {
        var (station, _) = await database.SeedAssignedStationAsync();
        var (_, otherOperator) = await database.SeedAssignedStationAsync();

        var result = await StartPhotoUploadAsync(otherOperator, station.PublicId, station.GalleryVersion);

        Assert.True(result.HasError<ForbiddenError>());
    }

    [Fact]
    public async Task StartPhotoUpload_Driver_IsForbidden()
    {
        var (station, _) = await database.SeedAssignedStationAsync();
        var (_, driver) = await database.SeedDriverAsync();

        var result = await StartPhotoUploadAsync(driver, station.PublicId, station.GalleryVersion);

        Assert.True(result.HasError<ForbiddenError>());
    }

    [Theory]
    [InlineData("image/gif", 10, nameof(StartStationPhotoUploadRequest.ContentType))]
    [InlineData(Jpeg, 1001, nameof(StartStationPhotoUploadRequest.ByteLength))]
    public async Task StartPhotoUpload_OutsideFileLimits_RejectsTheField(
        string contentType, long byteLength, string field)
    {
        var (station, identity) = await database.SeedAssignedStationAsync();

        var result = await StartPhotoUploadAsync(
            identity,
            station.PublicId,
            station.GalleryVersion,
            contentType,
            byteLength);

        Assert.True(result.HasError<ValidationError>(out var errors));
        Assert.Equal(field, Assert.Single(errors).PropertyName);
        Assert.Equal(0, await CountItemsAsync(station));
    }

    [Fact]
    public async Task StartPhotoUpload_GalleryFull_ReportsTheLimit()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        await FillGalleryAsync(identity, station);

        var result = await StartPhotoUploadAsync(identity, station.PublicId, await GalleryVersionAsync(station));

        Assert.True(result.HasError<StationMediaLimitError>());
    }

    [Fact]
    public async Task StartPhotoUpload_StaleVersion_ConflictsWithoutStarting()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        Assert.True((await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).IsSuccess);

        var stale = await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion);

        Assert.True(stale.HasError<ConcurrencyConflictError>());
        Assert.Equal(1, await CountItemsAsync(station));
    }

    // Editors that reload after each conflict keep starting uploads until the limit stops them all.
    [Fact]
    public async Task StartPhotoUpload_ConcurrentEditors_NeverExceedTheLimit()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();

        var started = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            int successes = 0;
            while (true)
            {
                var editor = await _api!.InvokeAsync<Result<StationGalleryEditorResponse>>(
                    identity, new GetStationGalleryEditorRequest(station.PublicId));
                var result = await StartPhotoUploadAsync(identity, station.PublicId, editor.Value.GalleryVersion);
                if (result.HasError<StationMediaLimitError>())
                {
                    return successes;
                }

                if (result.IsFailed && !result.HasError<ConcurrencyConflictError>())
                {
                    throw new InvalidOperationException(result.Errors[0].Message);
                }

                successes += result.IsSuccess ? 1 : 0;
            }
        }));

        Assert.Equal(MaximumPhotos, started.Sum());
        Assert.Equal(MaximumPhotos, await CountItemsAsync(station));
    }

    [Fact]
    public async Task CompleteUpload_UploadingPhoto_RequestsProcessingOnce()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var upload = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;
        var completion = new CompleteStationMediaUploadCommand(station.PublicId, upload.ItemId);

        Result<StationMediaUploadStateResponse>? first = null;
        Result<StationMediaUploadStateResponse>? repeated = null;
        await _api!.Messaging.TrackActivity().Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(async Task (IMessageContext _) =>
            {
                first = await _api.InvokeAsync<Result<StationMediaUploadStateResponse>>(identity, completion);
                repeated = await _api.InvokeAsync<Result<StationMediaUploadStateResponse>>(identity, completion);
            });

        Assert.Equal(StationMediaState.Processing, first!.Value.State);
        Assert.Equal(StationMediaState.Processing, repeated!.Value.State);
        await using var context = database.CreateContext();
        var item = await context.StationMediaItems.SingleAsync(item => item.PublicId == upload.ItemId);
        Assert.Equal(StationMediaState.Processing, item.State);
        Assert.True(await context.AuditEntries.AnyAsync(audit =>
            audit.ResourceType == AuditResourceType.StationMediaItem && audit.ResourceId == item.Id
            && audit.Action == AuditAction.Updated));
        Assert.Single(ProcessingRequests, id => id == item.Id);
    }

    [Fact]
    public async Task CompleteUpload_FailedPhoto_ReportsTheFailureAndFreesTheGalleryPlace()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var uploads = await FillGalleryAsync(identity, station);
        await SettleAsync(uploads[^1].ItemId, item => item.ResolveExpiredUpload(fileExists: false));

        var completion = new CompleteStationMediaUploadCommand(station.PublicId, uploads[^1].ItemId);
        var result = await _api!.InvokeAsync<Result<StationMediaUploadStateResponse>>(identity, completion);
        var repeated = await _api.InvokeAsync<Result<StationMediaUploadStateResponse>>(identity, completion);

        Assert.True(result.HasError<StationMediaUploadFailedError>());
        Assert.True(repeated.HasError<StationMediaUploadFailedError>());
        Assert.True((await StartPhotoUploadAsync(
            identity, station.PublicId, await GalleryVersionAsync(station))).IsSuccess);
    }

    [Fact]
    public async Task CompleteUpload_OperatorOfAnotherStation_IsForbidden()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var (_, otherOperator) = await database.SeedAssignedStationAsync();
        var upload = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;

        var result = await _api!.InvokeAsync<Result<StationMediaUploadStateResponse>>(
            otherOperator, new CompleteStationMediaUploadCommand(station.PublicId, upload.ItemId));

        Assert.True(result.HasError<ForbiddenError>());
    }

    [Fact]
    public async Task StartPhotoUpload_ReplacingAPublishedPhoto_WaitsWithoutCountingTowardTheLimit()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var uploads = await FillGalleryAsync(identity, station);
        await PublishAsync(uploads[0].ItemId);

        var result = await StartPhotoUploadAsync(
            identity, station.PublicId, await GalleryVersionAsync(station), replacesItemId: uploads[0].ItemId);

        Assert.True(result.IsSuccess);
        await using var context = database.CreateContext();
        var replacement = await context.StationMediaItems.SingleAsync(item => item.PublicId == result.Value.ItemId);
        var replaced = await context.StationMediaItems.SingleAsync(item => item.PublicId == uploads[0].ItemId);
        Assert.Equal(replaced.Id, replacement.ReplacesItemId);
        Assert.Null(replacement.Position);
        Assert.Equal(StationMediaState.Uploading, replacement.State);
        Assert.Equal(StationMediaState.Published, replaced.State);
        Assert.Equal(MaximumPhotos + 1, await CountItemsAsync(station));
    }

    [Fact]
    public async Task StartPhotoUpload_ReplacingAgain_DropsTheUnfinishedReplacementAndItsFiles()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var published = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;
        await PublishAsync(published.ItemId);
        var first = (await StartPhotoUploadAsync(
            identity, station.PublicId, await GalleryVersionAsync(station), replacesItemId: published.ItemId)).Value;

        var second = (await StartPhotoUploadAsync(
            identity, station.PublicId, await GalleryVersionAsync(station), replacesItemId: published.ItemId)).Value;

        await using var context = database.CreateContext();
        Assert.False(await context.StationMediaItems.AnyAsync(item => item.PublicId == first.ItemId));
        Assert.True(await context.StationMediaItems.AnyAsync(item => item.PublicId == second.ItemId));
        await WaitForAsync(DeletionRequests, first.ItemId);
    }

    [Fact]
    public async Task StartPhotoUpload_ReplacingAPhotoThatIsNotPublished_ConflictsWithoutStarting()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var pending = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;

        var result = await StartPhotoUploadAsync(
            identity, station.PublicId, pending.GalleryVersion, replacesItemId: pending.ItemId);

        Assert.True(result.HasError<StationMediaStateError>());
        Assert.Equal(1, await CountItemsAsync(station));
    }

    [Fact]
    public async Task StartPhotoUpload_ReplacingAnUnknownPhoto_IsNotFound()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();

        var result = await StartPhotoUploadAsync(
            identity, station.PublicId, station.GalleryVersion, replacesItemId: Guid.NewGuid());

        Assert.True(result.HasError<NotFoundError<Guid>>());
    }

    [Fact]
    public async Task Reorder_EveryPhoto_RewritesTheOrderAndAnnouncesTheChange()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var uploads = await FillGalleryAsync(identity, station);
        var version = await GalleryVersionAsync(station);

        var result = await _api!.InvokeAsync<Result<StationGalleryVersionResponse>>(
            identity,
            new ReorderStationGalleryCommand(
                station.PublicId,
                new ReorderStationGalleryRequest(version, [uploads[1].ItemId, uploads[0].ItemId])));

        Assert.Equal(version + 1, result.Value.GalleryVersion);
        var editor = await EditorAsync(identity, station);
        Assert.Equal([uploads[1].ItemId, uploads[0].ItemId], editor.Items.Select(item => item.Id));
        Assert.Equal([0, 1], editor.Items.Select(item => item.Position));
        await WaitForAsync(StationChanges, station.Id);
    }

    [Fact]
    public async Task Reorder_StaleVersionOrWrongPhotos_ChangesNothing()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var uploads = await FillGalleryAsync(identity, station);
        var version = await GalleryVersionAsync(station);

        var stale = await ReorderAsync(identity, station, version - 1, [uploads[1].ItemId, uploads[0].ItemId]);
        var missing = await ReorderAsync(identity, station, version, [uploads[1].ItemId]);
        var foreign = await ReorderAsync(identity, station, version, [uploads[1].ItemId, Guid.NewGuid()]);

        Assert.True(stale.HasError<ConcurrencyConflictError>());
        Assert.True(missing.HasError<ValidationError>());
        Assert.True(foreign.HasError<ValidationError>());
        var editor = await EditorAsync(identity, station);
        Assert.Equal(version, editor.GalleryVersion);
        Assert.Equal([uploads[0].ItemId, uploads[1].ItemId], editor.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Reorder_OperatorOfAnotherStation_IsForbidden()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var (_, otherOperator) = await database.SeedAssignedStationAsync();
        var uploads = await FillGalleryAsync(identity, station);

        var result = await ReorderAsync(
            otherOperator,
            station,
            await GalleryVersionAsync(station),
            [uploads[1].ItemId, uploads[0].ItemId]);

        Assert.True(result.HasError<ForbiddenError>());
    }

    [Fact]
    public async Task Remove_PublishedPhoto_HidesItAtOnceAndCleansUpItsFiles()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var uploads = await FillGalleryAsync(identity, station);
        await PublishAsync(uploads[0].ItemId);
        var version = await GalleryVersionAsync(station);
        Guid removedId;
        await using (var context = database.CreateContext())
        {
            removedId = await context.StationMediaItems.Where(item => item.PublicId == uploads[0].ItemId)
                .Select(item => item.Id)
                .SingleAsync();
        }

        var result = await RemoveAsync(identity, station, uploads[0].ItemId);

        Assert.Equal(version + 1, result.Value.GalleryVersion);
        var editor = await EditorAsync(identity, station);
        Assert.Equal([uploads[1].ItemId], editor.Items.Select(item => item.Id));
        await WaitForAsync(DeletionRequests, uploads[0].ItemId);
        await WaitForAsync(StationChanges, station.Id);
        await using var audits = database.CreateContext();
        Assert.True(await audits.AuditEntries.AnyAsync(audit =>
            audit.ResourceType == AuditResourceType.StationMediaItem && audit.ResourceId == removedId
            && audit.Action == AuditAction.Deleted));
    }

    [Fact]
    public async Task Remove_PublishedPhotoWithAReplacement_RemovesBoth()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var published = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;
        await PublishAsync(published.ItemId);
        var replacement = (await StartPhotoUploadAsync(
            identity, station.PublicId, await GalleryVersionAsync(station), replacesItemId: published.ItemId)).Value;

        Assert.True((await RemoveAsync(identity, station, published.ItemId)).IsSuccess);

        Assert.Equal(0, await CountItemsAsync(station));
        await WaitForAsync(DeletionRequests, replacement.ItemId);
    }

    [Fact]
    public async Task Remove_FailedPhoto_FreesItsPositionAndRepeatsAreHarmless()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var uploads = await FillGalleryAsync(identity, station);
        await SettleAsync(uploads[0].ItemId, item => item.ResolveExpiredUpload(fileExists: false));

        Assert.True((await RemoveAsync(identity, station, uploads[0].ItemId)).IsSuccess);
        Assert.True((await RemoveAsync(identity, station, uploads[0].ItemId)).IsSuccess);

        Assert.Equal(1, await CountItemsAsync(station));
    }

    [Fact]
    public async Task Remove_ConcurrentRequests_LeaveTheGalleryConsistent()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var uploads = await FillGalleryAsync(identity, station);

        var results = await Task.WhenAll(uploads.Select(upload => RemoveAsync(identity, station, upload.ItemId)));

        Assert.All(results, result => Assert.True(result.IsSuccess || result.HasError<ConcurrencyConflictError>()));
        var remaining = (await EditorAsync(identity, station)).Items.Count;
        Assert.Equal(results.Count(result => result.IsFailed), remaining);
    }

    [Fact]
    public async Task Remove_OperatorOfAnotherStation_IsForbidden()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var (_, otherOperator) = await database.SeedAssignedStationAsync();
        var upload = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;

        var result = await RemoveAsync(otherOperator, station, upload.ItemId);

        Assert.True(result.HasError<ForbiddenError>());
        Assert.Equal(1, await CountItemsAsync(station));
    }

    [Fact]
    public async Task Editor_PublishedPhotoWithAReplacement_ShowsThumbnailAndReplacementState()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var published = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;
        await PublishAsync(published.ItemId);
        var pending = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion + 1)).Value;
        var replacement = (await StartPhotoUploadAsync(
            identity, station.PublicId, pending.GalleryVersion, replacesItemId: published.ItemId)).Value;

        var editor = await EditorAsync(identity, station);

        var photo = editor.Items[0];
        Assert.Equal((published.ItemId, StationMediaState.Published), (photo.Id, photo.State));
        Assert.Equal(
            objectStore.ApiStore.PublicUrl(StationMediaObjectKeys.Variant(
                station.PublicId, published.ItemId, StationMediaObjectKeys.ThumbnailVariant)),
            photo.ThumbnailUrl);
        Assert.Equal(
            (replacement.ItemId, StationMediaState.Uploading), (photo.Replacement!.Id, photo.Replacement.State));
        var waiting = editor.Items[1];
        Assert.Equal((pending.ItemId, StationMediaState.Uploading), (waiting.Id, waiting.State));
        Assert.Null(waiting.ThumbnailUrl);
        Assert.Equal(2, editor.Items.Count);
    }

    private Task<Result<StationPhotoUploadResponse>> StartPhotoUploadAsync(
        UserIdentity identity,
        Guid stationId,
        int version,
        string contentType = Jpeg,
        long? byteLength = null,
        Guid? replacesItemId = null) =>
        _api!.InvokeAsync<Result<StationPhotoUploadResponse>>(
            identity,
            new StartStationPhotoUploadCommand(
                stationId,
                new StartStationPhotoUploadRequest(version, contentType, byteLength ?? Photo.Length, replacesItemId)));

    private Task<Result<StationGalleryVersionResponse>> ReorderAsync(
        UserIdentity identity, Station station, int version, IReadOnlyList<Guid> itemIds) =>
        _api!.InvokeAsync<Result<StationGalleryVersionResponse>>(
            identity,
            new ReorderStationGalleryCommand(station.PublicId, new ReorderStationGalleryRequest(version, itemIds)));

    private Task<Result<StationGalleryVersionResponse>> RemoveAsync(
        UserIdentity identity, Station station, Guid itemId) =>
        _api!.InvokeAsync<Result<StationGalleryVersionResponse>>(
            identity, new RemoveStationMediaCommand(station.PublicId, itemId));

    private async Task<StationGalleryEditorResponse> EditorAsync(UserIdentity identity, Station station) =>
        (await _api!.InvokeAsync<Result<StationGalleryEditorResponse>>(
            identity, new GetStationGalleryEditorRequest(station.PublicId))).Value;

    private async Task<List<StationPhotoUploadResponse>> FillGalleryAsync(UserIdentity identity, Station station)
    {
        var uploads = new List<StationPhotoUploadResponse>();
        var version = station.GalleryVersion;
        for (int photo = 0; photo < MaximumPhotos; photo++)
        {
            var upload = (await StartPhotoUploadAsync(identity, station.PublicId, version)).Value;
            uploads.Add(upload);
            version = upload.GalleryVersion;
        }

        return uploads;
    }

    private async Task<int> GalleryVersionAsync(Station station)
    {
        await using var context = database.CreateContext();
        return await context.Stations.Where(value => value.Id == station.Id)
            .Select(value => value.GalleryVersion)
            .SingleAsync();
    }

    // Stands in for the media worker, which publishes a photo once it has processed the upload.
    private Task PublishAsync(Guid itemId) => SettleAsync(
        itemId,
        item =>
        {
            item.CompleteUpload();
            item.Publish(null, _clock.GetUtcNow());
        });

    private async Task SettleAsync(Guid itemId, Action<StationMediaItem> change)
    {
        await using var context = database.CreateContext();
        var item = await context.StationMediaItems.SingleAsync(item => item.PublicId == itemId);
        change(item);
        await context.SaveChangesAsync();
    }

    private static Task WaitForAsync(ConcurrentQueue<Guid> messages, Guid expected) =>
        Polling.WaitUntilAsync(() => Task.FromResult(messages.Contains(expected)), TimeSpan.FromSeconds(30));

    private async Task<int> CountItemsAsync(Station station)
    {
        await using var context = database.CreateContext();
        return await context.StationMediaItems.CountAsync(item => item.StationId == station.Id);
    }

    public static class MessageCapture
    {
        public static void Handle(ProcessStationMediaMessage message) => ProcessingRequests.Enqueue(message.ItemId);

        public static void Handle(CheckStationMediaUploadMessage message)
        {
        }

        public static void Handle(DeleteStationMediaObjectsMessage message) =>
            DeletionRequests.Enqueue(message.ItemPublicId);

        public static void Handle(StationChangedEvent message) => StationChanges.Enqueue(message.StationId);
    }
}
