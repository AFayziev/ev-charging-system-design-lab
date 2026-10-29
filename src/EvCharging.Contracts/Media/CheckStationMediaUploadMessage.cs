namespace EvCharging.Contracts.Media;

/// <summary>Scheduled for a photo's upload deadline, to settle an upload that is still unfinished.</summary>
public sealed record CheckStationMediaUploadMessage(Guid ItemId);
