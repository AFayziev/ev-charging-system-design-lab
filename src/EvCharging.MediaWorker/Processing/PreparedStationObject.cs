namespace EvCharging.MediaWorker.Processing;

/// <summary>A verified object ready to be written to the public bucket under its immutable key.</summary>
public sealed record PreparedStationObject(string Key, Stream Content, string ContentType);
