using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Persistence;
using EvCharging.Persistence;
using EvCharging.Persistence.Auditing;
using EvCharging.Testing;
using JasperFx;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace EvCharging.Api.Tests;

// An API instance reduced to one handler, wired as the API wires it: the audited write route, the message store,
// and the application's handler policies. Separate instances share the database but keep their own clocks.
internal sealed class ApiHandlerHost : IAsyncDisposable
{
    private readonly IHost _host;

    private ApiHandlerHost(IHost host) => _host = host;

    public IHost Messaging => _host;

    // Tests inject database failures by registering further interceptors as IInterceptor services, and add message
    // routes and handlers through the messaging callback.
    public static async Task<ApiHandlerHost> StartAsync<THandler>(
        PlatformDatabaseFixture fixture,
        TimeProvider clock,
        Action<IServiceCollection>? configure = null,
        Action<WolverineOptions>? messaging = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAuditActorAccessor, HttpAuditActorAccessor>();
        builder.Services.AddScoped<AuditEntryWriter>();
        builder.Services.AddScoped<AuditSaveChangesInterceptor>();
        builder.Services.AddDbContext<PlatformDbContext>((services, options) =>
            options.UseNpgsql(fixture.GetDataSource("platform_writer"))
                .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>())
                .AddInterceptors(services.GetServices<IInterceptor>()));
        builder.Services.AddSingleton(clock);
        configure?.Invoke(builder.Services);
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.UseEntityFrameworkCoreTransactions();
            options.PersistMessagesWithPostgresql(fixture.GetConnectionString("platform_migrator"), "api_messages");
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.Discovery.IncludeType<THandler>();
            options.AddApplicationHandlers();
            messaging?.Invoke(options);
        });
        var host = builder.Build();
        await host.StartAsync();
        return new ApiHandlerHost(host);
    }

    // Dispatches as an endpoint does, so the request user and the API's error policies apply. Each call runs in its
    // own handler scope, and so on its own context and connection, as a separate request would. The method stays
    // async so the request user, held in an async local, stays with this call when calls run concurrently.
    public async Task<TResponse> InvokeAsync<TResponse>(UserIdentity identity, object request)
    {
        _host.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Iss, identity.Issuer),
                    new Claim(JwtRegisteredClaimNames.Sub, identity.Subject)
                ],
                "Test"))
        };
        return await _host.Services.GetRequiredService<IMessageBus>().InvokeAsync<TResponse>(request);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
