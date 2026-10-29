using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Stations;
using EvCharging.Domain.Media;
using EvCharging.Persistence;
using EvCharging.S3ObjectStore;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EvCharging.Api.Features.StationMedia;

public sealed record GetStationGalleryEditorRequest(Guid Id) : IAuthenticatedRequest<StationGalleryEditorResponse>;

public sealed record StationGalleryEditorResponse(
    Guid StationId,
    int GalleryVersion,
    IReadOnlyList<StationGalleryItemResponse> Items,
    StationMediaLimitsResponse Limits);

/// <summary>A photo of the gallery in order, with the replacement that is waiting to take its place.</summary>
public sealed record StationGalleryItemResponse(
    Guid Id,
    StationMediaKind Kind,
    int Position,
    StationMediaState State,
    string? FailureCode,
    Uri? ThumbnailUrl,
    StationGalleryReplacementResponse? Replacement);

public sealed record StationGalleryReplacementResponse(Guid Id, StationMediaState State, string? FailureCode);

public sealed record StationMediaLimitsResponse(
    int MaximumPhotosPerStation,
    long MaximumPhotoBytes,
    long MaximumPhotoPixels,
    IReadOnlyList<string> PhotoContentTypes,
    double UploadUrlLifetimeSeconds);

public sealed class GetStationGalleryEditorQueryHandler(
    PlatformDbContext database,
    IAuthorizationService authorization,
    ObjectStore objectStore,
    IOptionsSnapshot<StationMediaOptions> mediaOptions)
{
    public async Task<Result<StationGalleryEditorResponse>> HandleAsync(
        GetStationGalleryEditorRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .AsNoTracking()
            .SingleOrDefaultAsync(station => station.PublicId == request.Id, cancellationToken);
        if (station is null)
        {
            return Result.Fail<StationGalleryEditorResponse>(new NotFoundError<Guid>("station", request.Id));
        }

        var permission = await authorization.AuthorizeAsync(user, station, [StationAssignmentRequirement.Instance]);
        if (!permission.Succeeded)
        {
            return Result.Fail<StationGalleryEditorResponse>(new ForbiddenError());
        }

        var stationMediaItems = await database.StationMediaItems
            .AsNoTracking()
            .Where(item => item.StationId == station.Id)
            .Select(item => new GalleryRow(
                item.Id,
                item.PublicId,
                item.Kind,
                item.Position,
                item.ReplacesItemId,
                item.State,
                item.FailureCode))
            .ToListAsync(cancellationToken);
        var replacements = stationMediaItems
            .Where(row => row.ReplacesItemId is not null)
            .ToDictionary(row => row.ReplacesItemId!.Value);
        var items = stationMediaItems
            .Where(row => row.Position is not null)
            .OrderBy(row => row.Position)
            .Select(row => new StationGalleryItemResponse(
                row.PublicId,
                row.Kind,
                row.Position!.Value,
                row.State,
                row.FailureCode,
                row.State == StationMediaState.Published
                    ? objectStore.PublicUrl(StationMediaObjectKeys.Variant(
                        station.PublicId, row.PublicId, StationMediaObjectKeys.ThumbnailVariant))
                    : null,
                replacements.TryGetValue(row.Id, out var replacement)
                    ? new StationGalleryReplacementResponse(
                        replacement.PublicId, replacement.State, replacement.FailureCode)
                    : null))
            .ToList();

        return Result.Ok(new StationGalleryEditorResponse(
            station.PublicId,
            station.GalleryVersion,
            items,
            mediaOptions.Value.ToResponse()));
    }

    private sealed record GalleryRow(
        Guid Id,
        Guid PublicId,
        StationMediaKind Kind,
        int? Position,
        Guid? ReplacesItemId,
        StationMediaState State,
        string? FailureCode);
}

internal static class StationMediaLimitsMappings
{
    public static StationMediaLimitsResponse ToResponse(this StationMediaOptions options) => new(
        options.MaximumPhotosPerStation,
        options.MaximumPhotoBytes,
        options.MaximumPhotoPixels,
        options.PhotoContentTypes,
        options.UploadUrlLifetime.TotalSeconds);
}
