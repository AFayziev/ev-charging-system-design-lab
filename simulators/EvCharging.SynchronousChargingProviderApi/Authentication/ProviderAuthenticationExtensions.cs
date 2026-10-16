using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EvCharging.SynchronousChargingProviderApi.Authentication;

internal static class ProviderAuthenticationExtensions
{
    public const string SimulationControlPolicy = "simulation-control";

    public static IHostApplicationBuilder AddProviderAuthentication(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<ProviderAuthenticationOptions>()
            .BindConfiguration("ProviderAuthentication")
            .ValidateDataAnnotations()
            .Validate(
                settings => Uri.TryCreate(settings.Authority, UriKind.Absolute, out var authority)
                    && authority.UserInfo.Length == 0 && authority.Query.Length == 0 && authority.Fragment.Length == 0
                    && (authority.Scheme == "https"
                        || (settings.AllowLocalHttp && authority.IsLoopback && authority.Scheme == "http")),
                "Authority requires HTTPS or explicit loopback HTTP.")
            .ValidateOnStart();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ProviderAuthenticationOptions>>((options, settings) =>
            {
                options.Authority = settings.Value.Authority;
                options.Audience = settings.Value.Audience;
                options.RequireHttpsMetadata = new Uri(settings.Value.Authority).Scheme == "https";
                options.MapInboundClaims = false;
                options.TokenValidationParameters.RoleClaimType = "role";
            });
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole("charging-client")
                .Build();
            options.AddPolicy(
                SimulationControlPolicy,
                policy => policy.RequireAuthenticatedUser().RequireRole("simulation-admin"));
        });
        return builder;
    }
}
