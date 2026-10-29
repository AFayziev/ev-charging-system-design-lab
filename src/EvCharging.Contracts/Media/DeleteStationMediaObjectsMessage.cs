namespace EvCharging.Contracts.Media;

/// <summary>Requests deletion of every stored object of a photo that was removed or replaced.</summary>
public sealed record DeleteStationMediaObjectsMessage(Guid StationPublicId, Guid ItemPublicId);
