namespace EvCharging.Contracts.Media;

/// <summary>Requests processing of a station photo whose upload has been completed.</summary>
public sealed record ProcessStationMediaMessage(Guid ItemId);
