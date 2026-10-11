namespace EvCharging.Domain.Identity;

public sealed class Driver
{
    public Guid Id { get; set; }
    public required string IdentityIssuer { get; set; }
    public required string IdentitySubject { get; set; }
}
