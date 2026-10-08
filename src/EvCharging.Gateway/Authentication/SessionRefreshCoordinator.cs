using EvCharging.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EvCharging.Gateway.Authentication;

public sealed class SessionRefreshCoordinator(
    IDbContextFactory<AuthenticationDbContext> contexts,
    ITicketStore tickets,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    IOptions<SessionStoreOptions> options,
    ILogger<SessionRefreshCoordinator> logger)
{
    private static readonly TimeSpan DatabaseWriteTimeout = TimeSpan.FromSeconds(5);
    private readonly SessionTicketProtection _protection = new(dataProtection);

    /// <summary>
    /// Refreshes only the observed ticket version. The callback must perform one bounded provider call
    /// without automatic retries. Busy callers must reread before trying again; expired claims force re-login.
    /// </summary>
    public async Task<SessionRefreshResult> RefreshAsync(string key, AuthenticationTicket observed,
        Func<AuthenticationTicket, CancellationToken, Task<TokenRefreshResponse>> refresh,
        CancellationToken cancellationToken = default)
    {
        var observedVersion = SessionTicketProtection.GetVersion(observed)
            ?? throw new InvalidOperationException("Refresh requires a ticket retrieved from the shared session store.");
        if (!string.Equals(SessionTicketMetadata.GetSessionKey(observed), key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The observed ticket belongs to a different session.");
        }

        var current = await tickets.RetrieveAsync(key, cancellationToken);
        if (current is null)
        {
            return new(SessionRefreshStatus.Removed, null);
        }

        var id = Guid.Parse(key);
        var claim = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        var leaseDuration = TimeSpan.FromSeconds(options.Value.RefreshLeaseSeconds);
        var leaseExpiry = now + leaseDuration;
        AuthenticationSession? claimed;
        await using (var database = await contexts.CreateDbContextAsync(cancellationToken))
        {
            var changed = await database.Sessions.Where(session => session.Id == id
                && session.Version == observedVersion && session.RefreshClaim == null && session.ExpiresAt > now)
                .ExecuteUpdateAsync(update => update.SetProperty(session => session.RefreshClaim, claim)
                    .SetProperty(session => session.RefreshLeaseExpiresAt, leaseExpiry)
                    .SetProperty(session => session.Version, session => session.Version + 1), cancellationToken);
            claimed = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(session => session.Id == id, cancellationToken);
            if (changed == 0)
            {
                now = timeProvider.GetUtcNow();
                if (claimed is not null && (claimed.ExpiresAt <= now || claimed.RefreshLeaseExpiresAt <= now))
                {
                    await database.Sessions.Where(session => session.Id == id
                        && (session.ExpiresAt <= now || session.RefreshLeaseExpiresAt <= now)).ExecuteDeleteAsync(cancellationToken);
                    return new(SessionRefreshStatus.Removed, null);
                }

                return claimed is null ? new(SessionRefreshStatus.Removed, null)
                    : new(claimed.RefreshClaim is null ? SessionRefreshStatus.Current : SessionRefreshStatus.Busy,
                        _protection.Unprotect(claimed.ProtectedTicket, claimed.Version, key));
            }
        }

        if (claimed is null || claimed.RefreshClaim != claim)
        {
            return new(SessionRefreshStatus.Removed, null);
        }

        var remainingLease = leaseExpiry - timeProvider.GetUtcNow();
        if (remainingLease <= TimeSpan.Zero)
        {
            await RemoveClaimAsync(id, claim, cancellationToken);
            return new(SessionRefreshStatus.Removed, null);
        }

        // The claim persists before external work; no database connection is retained during the callback.
        using var leaseCancellation = new CancellationTokenSource(remainingLease, timeProvider);
        using var callbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, leaseCancellation.Token);
        TokenRefreshResponse response;
        byte[] payload;
        DateTimeOffset expiry;
        try
        {
            callbackCancellation.Token.ThrowIfCancellationRequested();
            response = await refresh(_protection.Unprotect(claimed.ProtectedTicket, claimed.Version, key), callbackCancellation.Token);
            payload = response.Ticket is null ? claimed.ProtectedTicket : _protection.Protect(response.Ticket);
            expiry = response.Ticket is null ? claimed.ExpiresAt : SessionTicketProtection.GetExpiry(response.Ticket);
        }
        catch
        {
            // Cleanup has a separate budget: request cancellation cannot authorize reuse of the old token.
            using var cleanup = new CancellationTokenSource(DatabaseWriteTimeout, timeProvider);
            try
            {
                await RemoveClaimAsync(id, claim, cleanup.Token);
            }
            catch (Exception cleanupError)
            {
                logger.LogWarning(cleanupError, "Refresh invalidation failed; the durable claim remains fenced until expiry.");
            }

            throw;
        }

        using var completion = new CancellationTokenSource(DatabaseWriteTimeout, timeProvider);
        now = timeProvider.GetUtcNow();
        await using (var database = await contexts.CreateDbContextAsync(completion.Token))
        {
            var changed = await database.Sessions.Where(session => session.Id == id && session.RefreshClaim == claim
                && session.Version == claimed.Version && session.ExpiresAt > now && session.RefreshLeaseExpiresAt > now && expiry > now)
                .ExecuteUpdateAsync(update => update.SetProperty(session => session.ProtectedTicket, payload)
                    .SetProperty(session => session.ExpiresAt, expiry)
                    .SetProperty(session => session.RefreshClaim, (Guid?)null)
                    .SetProperty(session => session.RefreshLeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(session => session.Version, session => session.Version + 1), completion.Token);
            if (changed == 0)
            {
                await database.Sessions.Where(session => session.Id == id && session.RefreshClaim == claim)
                    .ExecuteDeleteAsync(completion.Token);
                return new(SessionRefreshStatus.Removed, null);
            }
        }

        return new(response.Ticket is null ? SessionRefreshStatus.NoEffect : SessionRefreshStatus.Refreshed,
            _protection.Unprotect(payload, claimed.Version + 1, key));
    }

    private async Task RemoveClaimAsync(Guid id, Guid claim, CancellationToken cancellationToken)
    {
        await using var database = await contexts.CreateDbContextAsync(cancellationToken);
        await database.Sessions.Where(session => session.Id == id && session.RefreshClaim == claim)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
