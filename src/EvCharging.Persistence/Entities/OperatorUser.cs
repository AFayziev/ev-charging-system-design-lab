namespace EvCharging.Persistence.Entities;

public sealed class OperatorUser
{
    public Guid Id { get; set; }
    public required string IdentityIssuer { get; set; }
    public required string IdentitySubject { get; set; }
}
