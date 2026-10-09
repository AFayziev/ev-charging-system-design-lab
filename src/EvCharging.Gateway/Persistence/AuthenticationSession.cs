namespace EvCharging.Gateway.Persistence;

public sealed class AuthenticationSession
{
    public Guid Id { get; set; }
    public required string ProtectedTicket { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
