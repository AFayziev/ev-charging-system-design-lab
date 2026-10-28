using System.Collections.Concurrent;
using System.Net;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.StationMedia;
using EvCharging.Api.Features.Stations;
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
                options.Discovery.IncludeType<CompleteStationMediaUploadCommandHandler>();
                options.Discovery.IncludeType<GetStationGalleryEditorQueryHandler>();
                options.Discovery.IncludeType(typeof(ProcessingCapture));
                options.PublishMessage<ProcessStationMediaMessage>().ToLocalQueue("station-media-processing-test");
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
        var version = await context.StationMediaVersions
            .SingleAsync(version => version.PublicId == upload.VersionId);
        Assert.Equal(StationMediaVersionState.AwaitingUpload, version.State);
        var item = await context.StationMediaItems.SingleAsync(
            item => item.Id == version.ItemId && item.Station!.PublicId == station.PublicId);
        Assert.Equal(upload.ItemId, item.PublicId);
        Assert.True(await context.AuditEntries.AnyAsync(audit =>
            audit.ResourceType == AuditResourceType.StationMediaVersion && audit.ResourceId == version.Id
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
        var version = station.GalleryVersion;
        for (int photo = 0; photo < MaximumPhotos; photo++)
        {
            version = (await StartPhotoUploadAsync(identity, station.PublicId, version)).Value.GalleryVersion;
        }

        var result = await StartPhotoUploadAsync(identity, station.PublicId, version);

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

    [Fact]
    public async Task StartPhotoUpload_AfterStationDetailsChanged_KeepsTheGalleryVersion()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        await using (var context = database.CreateContext())
        {
            await context.Stations
                .Where(value => value.Id == station.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Name, "Renamed"));
        }

        var result = await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion);

        Assert.True(result.IsSuccess);
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
    public async Task CompleteUpload_StoredMatchingObject_KeepsTheOriginalAndRequestsProcessingOnce()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var upload = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;
        (await objectStore.PutAsync(upload.UploadUrl, Photo, Jpeg)).EnsureSuccessStatusCode();
        var completion = new CompleteStationMediaUploadCommand(
            station.PublicId, upload.ItemId, upload.VersionId);

        Result<StationMediaVersionResponse>? first = null;
        Result<StationMediaVersionResponse>? repeated = null;
        await _api!.Messaging.TrackActivity().Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(async Task (IMessageContext _) =>
            {
                first = await _api.InvokeAsync<Result<StationMediaVersionResponse>>(identity, completion);
                repeated = await _api.InvokeAsync<Result<StationMediaVersionResponse>>(identity, completion);
            });

        Assert.Equal(StationMediaVersionState.Uploaded, first!.Value.State);
        Assert.Equal(StationMediaVersionState.Uploaded, repeated!.Value.State);
        var original = await objectStore.ApiStore.GetPrivateObjectAsync(
            StationMediaObjectKeys.Original(station.PublicId, upload.ItemId, upload.VersionId),
            CancellationToken.None);
        Assert.Equal(Photo.Length, original?.Length);
        await using var context = database.CreateContext();
        var version = await context.StationMediaVersions
            .SingleAsync(version => version.PublicId == upload.VersionId);
        Assert.Equal(StationMediaVersionState.Uploaded, version.State);
        Assert.True(await context.AuditEntries.AnyAsync(audit =>
            audit.ResourceType == AuditResourceType.StationMediaVersion && audit.ResourceId == version.Id
            && audit.Action == AuditAction.Updated));
        Assert.Single(ProcessingRequests, id => id == version.Id);
    }

    // An earlier notice kept the original but failed to save, and the upload has expired since.
    [Fact]
    public async Task CompleteUpload_OriginalKeptAndUploadExpired_RecordsTheOriginal()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var upload = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;
        (await objectStore.PutAsync(upload.UploadUrl, Photo, Jpeg)).EnsureSuccessStatusCode();
        var uploadKey = StationMediaObjectKeys.Upload(station.PublicId, upload.ItemId, upload.VersionId);
        var stored = await objectStore.ApiStore.GetPrivateObjectAsync(uploadKey, CancellationToken.None);
        await objectStore.ApiStore.CopyPrivateAsync(
            uploadKey,
            StationMediaObjectKeys.Original(station.PublicId, upload.ItemId, upload.VersionId),
            stored!.ETag,
            CancellationToken.None);
        await objectStore.ApiClient.DeleteObjectAsync(ObjectStoreFixture.PrivateBucket, uploadKey);

        var result = await _api!.InvokeAsync<Result<StationMediaVersionResponse>>(
            identity, new CompleteStationMediaUploadCommand(station.PublicId, upload.ItemId, upload.VersionId));

        Assert.Equal(StationMediaVersionState.Uploaded, result.Value.State);
    }

    [Fact]
    public async Task CompleteUpload_ExpiredWithoutUpload_FailsAndFreesTheGalleryPlace()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var version = station.GalleryVersion;
        StationPhotoUploadResponse? upload = null;
        for (var count = 0; count < MaximumPhotos; count++)
        {
            upload = (await StartPhotoUploadAsync(identity, station.PublicId, version)).Value;
            version = upload.GalleryVersion;
        }

        _clock.Advance(TimeSpan.FromMinutes(30));
        var completion = new CompleteStationMediaUploadCommand(station.PublicId, upload!.ItemId, upload.VersionId);
        var result = await _api!.InvokeAsync<Result<StationMediaVersionResponse>>(identity, completion);
        var repeated = await _api.InvokeAsync<Result<StationMediaVersionResponse>>(identity, completion);

        Assert.True(result.HasError<StationMediaUploadFailedError>());
        Assert.True(repeated.HasError<StationMediaUploadFailedError>());
        Assert.True((await StartPhotoUploadAsync(identity, station.PublicId, version)).IsSuccess);
    }

    [Fact]
    public async Task CompleteUpload_NothingUploaded_ReportsTheMissingUpload()
    {
        var (station, identity) = await database.SeedAssignedStationAsync();
        var upload = (await StartPhotoUploadAsync(identity, station.PublicId, station.GalleryVersion)).Value;

        var result = await _api!.InvokeAsync<Result<StationMediaVersionResponse>>(
            identity, new CompleteStationMediaUploadCommand(station.PublicId, upload.ItemId, upload.VersionId));

        Assert.True(result.HasError<StationMediaUploadMissingError>());
        await using var context = database.CreateContext();
        Assert.Equal(
            StationMediaVersionState.AwaitingUpload,
            (await context.StationMediaVersions.SingleAsync(version => version.PublicId == upload.VersionId))
            .State);
    }

    private Task<Result<StationPhotoUploadResponse>> StartPhotoUploadAsync(
        UserIdentity identity, Guid stationId, int version, string contentType = Jpeg, long? byteLength = null) =>
        _api!.InvokeAsync<Result<StationPhotoUploadResponse>>(
            identity,
            new StartStationPhotoUploadCommand(
                stationId, new StartStationPhotoUploadRequest(version, contentType, byteLength ?? Photo.Length)));

    private async Task<int> CountItemsAsync(Station station)
    {
        await using var context = database.CreateContext();
        return await context.StationMediaItems.CountAsync(item => item.StationId == station.Id);
    }

    public static class ProcessingCapture
    {
        public static void Handle(ProcessStationMediaMessage message) => ProcessingRequests.Enqueue(message.VersionId);
    }
}
