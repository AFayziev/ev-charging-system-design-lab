using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Stations;
using EvCharging.Domain.Media;
using EvCharging.Persistence;
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

public sealed record StationGalleryItemResponse(
    Guid Id,
    StationMediaKind Kind,
    int Position,
    Guid VersionId,
    StationMediaVersionState State);

public sealed record StationMediaLimitsResponse(
    int MaximumPhotosPerStation,
    long MaximumPhotoBytes,
    long MaximumPhotoPixels,
    IReadOnlyList<string> PhotoContentTypes,
    double UploadUrlLifetimeSeconds);

public sealed class GetStationGalleryEditorQueryHandler(
    PlatformDbContext database,
    IAuthorizationService authorization,
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
            .Where(item => item.StationId == station.Id)
            .OrderBy(item => item.Position)
            .Select(item => item.Versions
                .OrderByDescending(version => version.CreatedAt)
                .ThenByDescending(version => version.Id)
                .Select(version => new StationGalleryItemResponse(
                    item.PublicId, item.Kind, item.Position, version.PublicId, version.State))
                .First())
            .ToListAsync(cancellationToken);

        return Result.Ok(new StationGalleryEditorResponse(
            station.PublicId,
            station.GalleryVersion,
            stationMediaItems,
            mediaOptions.Value.ToResponse()));
    }
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
