using System.Security.Claims;
using EvCharging.Gateway.Authentication;
using EvCharging.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EvCharging.Gateway.Tests;

public sealed class SessionStoreTests(AuthenticationDatabaseFixture database) : IClassFixture<AuthenticationDatabaseFixture>
{
    [Fact]
    public async Task Retrieve_SharedProtectedSession_PreservesIdentityAndExpires()
    {
        var clock = new FakeTimeProvider();
        using var first = CreateHost(clock);
        using var second = CreateHost(clock);
        var tickets = first.Services.GetRequiredService<ITicketStore>();
        var otherTickets = second.Services.GetRequiredService<ITicketStore>();
        var key = await tickets.StoreAsync(Ticket(clock));
        var retrieved = Assert.IsType<AuthenticationTicket>(await otherTickets.RetrieveAsync(key));
        Assert.Equal("refresh-secret", retrieved.Properties.GetTokenValue("refresh_token"));
        Assert.Equal("driver", retrieved.Principal.FindFirstValue(ClaimTypes.NameIdentifier));

        var contexts = second.Services.GetRequiredService<IDbContextFactory<AuthenticationDbContext>>();
        await using var context = await contexts.CreateDbContextAsync();
        var payload = await context.Sessions.Where(session => session.Id == Guid.Parse(key))
            .Select(session => session.ProtectedTicket).SingleAsync();
        Assert.DoesNotContain("refresh-secret", payload, StringComparison.Ordinal);

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(await otherTickets.RetrieveAsync(key));
    }

    private IHost CreateHost(TimeProvider clock)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:gateway-auth"] = database.ConnectionString;
        builder.Configuration["AuthenticationDatabase:MaximumPoolSize"] = "1";
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.AddAuthenticationPersistence();
        builder.AddAuthenticationSessions();
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
