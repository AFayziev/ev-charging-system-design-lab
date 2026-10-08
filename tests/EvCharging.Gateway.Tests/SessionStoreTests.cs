using System.Security.Claims;
using EvCharging.Gateway.Authentication;
using EvCharging.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EvCharging.Gateway.Tests;

public sealed class SessionStoreTests(AuthenticationDatabaseFixture database) : IClassFixture<AuthenticationDatabaseFixture>
{
    [Fact]
    public async Task Renew_ExpiredSession_DoesNotRevive()
    {
        var clock = new FakeTimeProvider();
        using var first = CreateHost(clock);
        using var second = CreateHost(clock);
        var tickets = first.Services.GetRequiredService<ITicketStore>();
        var otherTickets = second.Services.GetRequiredService<ITicketStore>();
        var original = Ticket(clock);
        var key = await tickets.StoreAsync(original);
        var retrieved = Assert.IsType<AuthenticationTicket>(await otherTickets.RetrieveAsync(key));
        Assert.Equal(key, SessionTicketMetadata.GetSessionKey(retrieved));
        Assert.Equal("refresh-secret", retrieved.Properties.GetTokenValue("refresh_token"));
        Assert.Equal("driver", retrieved.Principal.FindFirstValue(ClaimTypes.NameIdentifier));

        clock.Advance(TimeSpan.FromMinutes(5));
        retrieved.Properties.ExpiresUtc = clock.GetUtcNow() + TimeSpan.FromMinutes(10);
        await otherTickets.RenewAsync(key, retrieved);
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.NotNull(await tickets.RetrieveAsync(key));
        clock.Advance(TimeSpan.FromMinutes(4));
        retrieved.Properties.ExpiresUtc = clock.GetUtcNow() + TimeSpan.FromMinutes(10);
        await tickets.RenewAsync(key, retrieved);
        Assert.Null(await tickets.RetrieveAsync(key));
    }

    [Fact]
    public async Task Refresh_ConcurrentCalls_RotatesOnce()
    {
        var clock = new FakeTimeProvider();
        using var first = CreateHost(clock);
        using var second = CreateHost(clock);
        var tickets = first.Services.GetRequiredService<ITicketStore>();
        var otherTickets = second.Services.GetRequiredService<ITicketStore>();
        var key = await tickets.StoreAsync(Ticket(clock));
        var observed = Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(key));
        var stale = Assert.IsType<AuthenticationTicket>(await otherTickets.RetrieveAsync(key));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = first.Services.GetRequiredService<SessionRefreshCoordinator>().RefreshAsync(key, observed, async (_, _) =>
        {
            entered.SetResult();
            await finish.Task.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            return new(Ticket(clock, "rotated-secret"));
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var competing = await second.Services.GetRequiredService<SessionRefreshCoordinator>().RefreshAsync(key, stale,
            (_, _) => throw new InvalidOperationException("Competing replica must not contact the provider."));
        Assert.Equal(SessionRefreshStatus.Busy, competing.Status);
        var duringRefresh = Assert.IsType<AuthenticationTicket>(await otherTickets.RetrieveAsync(key));
        await otherTickets.RenewAsync(key, duringRefresh);
        finish.SetResult();
        Assert.Equal(SessionRefreshStatus.Refreshed, (await refresh).Status);
        await otherTickets.RenewAsync(key, stale);
        var current = Assert.IsType<AuthenticationTicket>(await otherTickets.RetrieveAsync(key));
        Assert.Equal("rotated-secret", current.Properties.GetTokenValue("refresh_token"));
        var after = await second.Services.GetRequiredService<SessionRefreshCoordinator>().RefreshAsync(key, stale,
            (_, _) => throw new InvalidOperationException("Old versions must not refresh again."));
        Assert.Equal(SessionRefreshStatus.Current, after.Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Refresh_InvalidatedSession_RejectsCompletion(bool expireLease, bool readBeforeCompletion)
    {
        const int RefreshLeaseSeconds = 10;

        var clock = new FakeTimeProvider();
        using var first = CreateHost(clock, RefreshLeaseSeconds);
        using var second = CreateHost(clock, RefreshLeaseSeconds);
        var tickets = first.Services.GetRequiredService<ITicketStore>();
        var otherTickets = second.Services.GetRequiredService<ITicketStore>();
        var key = await tickets.StoreAsync(Ticket(clock));
        var observed = Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(key));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = first.Services.GetRequiredService<SessionRefreshCoordinator>().RefreshAsync(key, observed, async (_, _) =>
        {
            entered.SetResult();
            await finish.Task.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            return new(Ticket(clock, "late-secret"));
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (expireLease)
        {
            clock.Advance(TimeSpan.FromSeconds(RefreshLeaseSeconds + 1));
            if (readBeforeCompletion)
            {
                Assert.Null(await otherTickets.RetrieveAsync(key));
            }
        }
        else
        {
            await otherTickets.RemoveAsync(key);
        }

        finish.SetResult();
        Assert.Equal(SessionRefreshStatus.Removed, (await refresh).Status);
        Assert.Null(await otherTickets.RetrieveAsync(key));
        await tickets.RenewAsync(key, observed);
        Assert.Null(await tickets.RetrieveAsync(key));
        var retry = await second.Services.GetRequiredService<SessionRefreshCoordinator>().RefreshAsync(key, observed,
            (_, _) => throw new InvalidOperationException("Uncertain refresh must never be repeated."));
        Assert.Equal(SessionRefreshStatus.Removed, retry.Status);
    }

    [Fact]
    public async Task Refresh_NoEffect_AllowsRetry()
    {
        var clock = new FakeTimeProvider();
        using var host = CreateHost(clock);
        var tickets = host.Services.GetRequiredService<ITicketStore>();
        var coordinator = host.Services.GetRequiredService<SessionRefreshCoordinator>();
        var key = await tickets.StoreAsync(Ticket(clock));
        var observed = Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(key));
        var released = await coordinator.RefreshAsync(key, observed, (_, _) => Task.FromResult(new TokenRefreshResponse(null)));
        Assert.Equal(SessionRefreshStatus.NoEffect, released.Status);
        await tickets.RenewAsync(key, observed);
        var stale = await coordinator.RefreshAsync(key, observed, (_, _) => throw new InvalidOperationException());
        Assert.Equal(SessionRefreshStatus.Current, stale.Status);
        var retry = await coordinator.RefreshAsync(key, Assert.IsType<AuthenticationTicket>(released.Ticket),
            (_, _) => Task.FromResult(new TokenRefreshResponse(Ticket(clock, "rotated-secret"))));
        Assert.Equal(SessionRefreshStatus.Refreshed, retry.Status);
        Assert.Equal("rotated-secret", Assert.IsType<AuthenticationTicket>(retry.Ticket).Properties.GetTokenValue("refresh_token"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_ProviderFailure_RemovesSession(bool cancel)
    {
        var clock = new FakeTimeProvider();
        using var host = CreateHost(clock);
        var tickets = host.Services.GetRequiredService<ITicketStore>();
        var key = await tickets.StoreAsync(Ticket(clock));
        var observed = Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(key));
        using var cancellation = new CancellationTokenSource();
        var coordinator = host.Services.GetRequiredService<SessionRefreshCoordinator>();
        var error = await Record.ExceptionAsync(() => coordinator.RefreshAsync(key, observed, (_, token) =>
        {
            if (cancel)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }

            throw new IOException("Provider response was lost.");
        }, cancellation.Token));
        if (cancel)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error);
        }
        else
        {
            Assert.IsType<IOException>(error);
        }

        Assert.Null(await tickets.RetrieveAsync(key));
    }

    [Fact]
    public async Task Renew_InvalidTicket_Throws()
    {
        var clock = new FakeTimeProvider();
        using var host = CreateHost(clock);
        var tickets = host.Services.GetRequiredService<ITicketStore>();
        var key = await tickets.StoreAsync(Ticket(clock));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tickets.RenewAsync(key, Ticket(clock, "replacement")));
        var otherKey = await tickets.StoreAsync(Ticket(clock, "other-session-secret"));
        var observed = Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(otherKey));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tickets.RenewAsync(key, observed));
        Assert.Equal("refresh-secret", Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(key)).Properties.GetTokenValue("refresh_token"));
    }

    private IHost CreateHost(TimeProvider clock, int? refreshLeaseSeconds = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:gateway-auth"] = database.ConnectionString;
        builder.Configuration["AuthenticationDatabase:MaximumPoolSize"] = "1";
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.AddAuthenticationPersistence();
        builder.AddAuthenticationSessions();
        if (refreshLeaseSeconds is not null)
        {
            builder.Services.Configure<SessionStoreOptions>(options => options.RefreshLeaseSeconds = refreshLeaseSeconds.Value);
        }

        return builder.Build();
    }

    private static AuthenticationTicket Ticket(TimeProvider clock, string refreshToken = "refresh-secret")
    {
        var properties = new AuthenticationProperties { ExpiresUtc = clock.GetUtcNow() + TimeSpan.FromMinutes(10) };
        properties.StoreTokens([new AuthenticationToken { Name = "refresh_token", Value = refreshToken }]);
        return new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "driver")], "session")), properties, "session");
    }
}
