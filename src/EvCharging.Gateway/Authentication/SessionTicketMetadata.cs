using Microsoft.AspNetCore.Authentication;

namespace EvCharging.Gateway.Authentication;

public static class SessionTicketMetadata
{
    internal const string KeyProperty = ".gateway.session-key";

    /// <summary>Returns the server-side session reference for refresh and logout; never expose it in browser responses.</summary>
    public static string? GetSessionKey(AuthenticationTicket ticket) =>
        ticket.Properties.Items.TryGetValue(KeyProperty, out var key) ? key : null;
}
