using EvCharging.Domain.Media;

namespace EvCharging.Api.Features.Stations;

/// <summary>
/// A published item of a station's gallery in order. A photo carries its variant URLs and a video carries the URL
/// of the published file.
/// </summary>
public sealed record StationMediaResponse(
    Guid Id, StationMediaKind Kind, Uri? ThumbnailUrl, Uri? DisplayUrl, Uri? VideoUrl);
