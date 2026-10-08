namespace EvCharging.Gateway.Persistence;

public sealed class AuthenticationSession
{
    public required string Id { get; set; }
    public required byte[] ProtectedTicket { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long Version { get; set; }
}
