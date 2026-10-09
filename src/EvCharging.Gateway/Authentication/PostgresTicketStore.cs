using EvCharging.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Gateway.Authentication;

internal sealed class PostgresTicketStore(
    IDbContextFactory<AuthenticationDbContext> contexts,
    IDataProtectionProvider protectionProvider,
    TimeProvider timeProvider) : ITicketStore
{
    private readonly TicketDataFormat _ticketFormat = new(
        protectionProvider.CreateProtector("EvCharging.Gateway.SessionTicket", "v1"));

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);
    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);
    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        await using var database = await contexts.CreateDbContextAsync(cancellationToken);
        var session = new AuthenticationSession
        {
            ProtectedTicket = _ticketFormat.Protect(ticket),
            ExpiresAt = GetExpiry(ticket)
        };
        database.Sessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);
        return session.Id.ToString();
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(key);
        var payload = _ticketFormat.Protect(ticket);
        var expiry = GetExpiry(ticket);
        await using var database = await contexts.CreateDbContextAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO gateway_auth.sessions (id, protected_ticket, expires_at)
            VALUES ({id}, {payload}, {expiry})
            ON CONFLICT (id) DO UPDATE
            SET protected_ticket = EXCLUDED.protected_ticket, expires_at = EXCLUDED.expires_at
            """, cancellationToken);
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
        if (session.ExpiresAt <= now)
        {
            await database.Sessions.Where(session => session.Id == id && session.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken);
            return null;
        }

        return _ticketFormat.Unprotect(session.ProtectedTicket);
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

    private static DateTimeOffset GetExpiry(AuthenticationTicket ticket) =>
        ticket.Properties.ExpiresUtc ?? throw new InvalidOperationException("Authentication tickets must have an expiry.");
}
