namespace EvCharging.Domain.Identity;

public sealed class Driver
{
    private Driver(string identityIssuer, string identitySubject, string email)
    {
        IdentityIssuer = identityIssuer;
        IdentitySubject = identitySubject;
        Email = email;
    }

    public Guid Id { get; private set; }
    public string IdentityIssuer { get; private set; }
    public string IdentitySubject { get; private set; }
    public string Email { get; private set; }

    public static Driver Register(string identityIssuer, string identitySubject, EmailAddress email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityIssuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(identitySubject);
        ArgumentNullException.ThrowIfNull(email);
        return new(identityIssuer, identitySubject, email.Value);
    }
}
