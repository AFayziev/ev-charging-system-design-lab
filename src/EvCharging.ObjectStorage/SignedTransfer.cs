namespace EvCharging.ObjectStorage;

/// <summary>A time-limited transfer permission for one object; not a stable file identity.</summary>
public sealed record SignedTransfer(Uri Url, DateTimeOffset ExpiresAt);
