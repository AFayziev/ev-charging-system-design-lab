namespace EvCharging.Gateway.Persistence;

public sealed class AuthenticationSession
{
    public Guid Id { get; set; }
    public required byte[] ProtectedTicket { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long Version { get; set; }
    public Guid? RefreshClaim { get; set; }
    public DateTimeOffset? RefreshLeaseExpiresAt { get; set; }
}
