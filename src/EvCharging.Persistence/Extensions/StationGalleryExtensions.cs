using EvCharging.Domain.Media;

namespace EvCharging.Persistence.Extensions;

public static class StationGalleryExtensions
{
    /// <summary>
    /// Published photos; the first by position is the cover. A replacement
    /// in progress has no position, so the photo it replaces stays visible until it is published.
    /// </summary>
    public static IQueryable<StationMediaItem> Visible(this IQueryable<StationMediaItem> media) => media
        .Where(item => item.State == StationMediaState.Published && item.Position != null);

    /// <summary>Visible photos only. A video is never a station's cover.</summary>
    public static IQueryable<StationMediaItem> VisiblePhotos(this IQueryable<StationMediaItem> media) => media
        .Visible()
        .Where(item => item.Kind == StationMediaKind.Photo);
}
