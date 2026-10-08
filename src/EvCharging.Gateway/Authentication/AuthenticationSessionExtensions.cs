using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EvCharging.Gateway.Authentication;

public static class AuthenticationSessionExtensions
{
    public static IHostApplicationBuilder AddAuthenticationSessions(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<SessionStoreOptions>()
            .Bind(builder.Configuration.GetSection("AuthenticationSessions"))
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddDataProtection().SetApplicationName("EvCharging.Gateway")
            .PersistKeysToDbContext<Persistence.AuthenticationDbContext>();
        builder.Services.AddSingleton<SessionTicketProtection>();
        builder.Services.AddSingleton<ITicketStore, PostgresTicketStore>();
        builder.Services.AddSingleton<SessionRefreshCoordinator>();
        return builder;
    }
}
