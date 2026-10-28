namespace EvCharging.Contracts.Media;

/// <summary>Requests processing of a station media version whose original has been verified.</summary>
public sealed record ProcessStationMediaMessage(Guid VersionId);
