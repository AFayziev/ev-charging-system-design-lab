using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace EvCharging.Gateway.Authentication;

internal sealed class SessionTicketProtection(IDataProtectionProvider provider)
{
    private const string VersionProperty = ".gateway.session-version";
    private readonly IDataProtector _protector = provider.CreateProtector("EvCharging.Gateway.SessionTicket", "v1");

    public byte[] Protect(AuthenticationTicket ticket)
    {
        var properties = new AuthenticationProperties(new Dictionary<string, string?>(ticket.Properties.Items));
        properties.Items.Remove(VersionProperty);
        properties.Items.Remove(SessionTicketMetadata.KeyProperty);
        return _protector.Protect(TicketSerializer.Default.Serialize(
            new AuthenticationTicket(ticket.Principal, properties, ticket.AuthenticationScheme)));
    }

    public AuthenticationTicket Unprotect(byte[] payload, long version, string key)
    {
        var ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(payload))
            ?? throw new InvalidOperationException("The stored authentication ticket is invalid.");
        SetVersion(ticket, version);
        ticket.Properties.Items[SessionTicketMetadata.KeyProperty] = key;
        return ticket;
    }

    public static void SetVersion(AuthenticationTicket ticket, long version) =>
        ticket.Properties.Items[VersionProperty] = version.ToString(CultureInfo.InvariantCulture);

    public static long? GetVersion(AuthenticationTicket ticket) =>
        ticket.Properties.Items.TryGetValue(VersionProperty, out var value)
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;

    public static DateTimeOffset GetExpiry(AuthenticationTicket ticket) =>
        ticket.Properties.ExpiresUtc ?? throw new InvalidOperationException("Authentication tickets must have an expiry.");
}
