namespace EvCharging.ObjectStorage;

/// <summary>
/// Object facts reported by storage, used to verify an upload before trusting it. The content type is null when
/// the object was stored without one; the ETag is unquoted.
/// </summary>
public sealed record StoredObject(long Length, string? ContentType, string ETag);
