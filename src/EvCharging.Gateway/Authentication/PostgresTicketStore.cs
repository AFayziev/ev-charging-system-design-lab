using EvCharging.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Gateway.Authentication;

internal sealed class PostgresTicketStore(
    IDbContextFactory<AuthenticationDbContext> contexts,
    SessionTicketProtection protection,
    TimeProvider timeProvider) : ITicketStore
{
    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);
    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);
    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        await using var database = await contexts.CreateDbContextAsync(cancellationToken);
        var session = new AuthenticationSession
        {
            ProtectedTicket = protection.Protect(ticket),
            ExpiresAt = SessionTicketProtection.GetExpiry(ticket),
            Version = 1
        };
        database.Sessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);

        var key = session.Id.ToString();
        SessionTicketProtection.SetVersion(ticket, 1);
        ticket.Properties.Items[SessionTicketMetadata.KeyProperty] = key;
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var version = SessionTicketProtection.GetVersion(ticket);
        if (version is null || !string.Equals(SessionTicketMetadata.GetSessionKey(ticket), key, StringComparison.Ordinal))
        {
            // Re-login must remove the previous session and store a new ticket.
            throw new InvalidOperationException("Renewal requires a ticket retrieved from the shared session store.");
        }

        var payload = protection.Protect(ticket);
        var expiry = SessionTicketProtection.GetExpiry(ticket);
        var id = Guid.Parse(key);
        var now = timeProvider.GetUtcNow();
        await using var database = await contexts.CreateDbContextAsync(cancellationToken);
        var changed = await database.Sessions
            .Where(session => session.Id == id && session.Version == version && session.ExpiresAt > now
                && session.RefreshClaim == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.ProtectedTicket, payload)
                .SetProperty(session => session.ExpiresAt, expiry)
                .SetProperty(session => session.Version, session => session.Version + 1), cancellationToken);
        if (changed == 1)
        {
            SessionTicketProtection.SetVersion(ticket, version.Value + 1);
        }
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return null;
        }

        await using var database = await contexts.CreateDbContextAsync(cancellationToken);
        var session = await database.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(session => session.Id == id, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (session.ExpiresAt <= now || session.RefreshLeaseExpiresAt <= now)
        {
            // An expired refresh lease can represent a completed, unrecorded token rotation.
            await database.Sessions.Where(session => session.Id == id
                && (session.ExpiresAt <= now || session.RefreshLeaseExpiresAt <= now)).ExecuteDeleteAsync(cancellationToken);
            return null;
        }

        return protection.Unprotect(session.ProtectedTicket, session.Version, key);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }

        await using var database = await contexts.CreateDbContextAsync(cancellationToken);
        await database.Sessions.Where(session => session.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
