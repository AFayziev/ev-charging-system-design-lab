namespace EvCharging.Api.Features.Stations;

/// <summary>A published photo, with the variants a card and a full view load.</summary>
public sealed record StationPhotoResponse(Guid Id, Uri ThumbnailUrl, Uri DisplayUrl);
