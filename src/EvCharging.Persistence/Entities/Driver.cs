namespace EvCharging.Persistence.Entities;

public sealed class Driver
{
    public long Id { get; set; }
    public required string IdentityIssuer { get; set; }
    public required string IdentitySubject { get; set; }
}
