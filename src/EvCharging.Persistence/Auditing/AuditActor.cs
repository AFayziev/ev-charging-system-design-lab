namespace EvCharging.Persistence.Auditing;

public sealed record AuditActor
{
    private AuditActor(
        AuditActorType type,
        string? issuer,
        string subject)
    {
        Type = type;
        Issuer = issuer;
        Subject = subject;
    }

    public AuditActorType Type { get; }
    public string? Issuer { get; }
    public string Subject { get; }

    public static AuditActor User(string issuer, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return new(AuditActorType.User, issuer, subject);
    }

    public static AuditActor Service(string authenticatedServiceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedServiceId);
        return new(AuditActorType.Service, null, authenticatedServiceId);
    }
}
