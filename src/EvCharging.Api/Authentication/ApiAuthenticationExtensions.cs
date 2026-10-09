using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EvCharging.Api.Authentication;

internal static class ApiAuthenticationExtensions
{
    public static IHostApplicationBuilder AddApiAuthentication(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<ApiAuthenticationOptions>()
            .Bind(builder.Configuration.GetSection("ApiAuthentication"))
            .ValidateDataAnnotations()
            .Validate(settings => Uri.TryCreate(settings.Authority, UriKind.Absolute, out var authority)
                && authority.Query.Length == 0 && authority.Fragment.Length == 0 && authority.UserInfo.Length == 0
                && (authority.Scheme == "https"
                    || (settings.AllowLocalHttp == true && authority.IsLoopback && authority.Scheme == "http")), "Authority requires HTTPS or explicit loopback HTTP.")
            .Validate(settings => Uri.TryCreate(settings.MetadataAddress, UriKind.Absolute, out var metadata)
                && (metadata.Scheme is "https" or "http") && metadata.UserInfo.Length == 0
                && metadata.Fragment.Length == 0, "MetadataAddress requires explicit HTTP or HTTPS backchannel transport.")
            .ValidateOnStart();
        builder.Services.AddHttpClient("jwt-backchannel");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ApiAuthenticationOptions>, IHttpClientFactory>((options, settings, clients) =>
            {
                options.Authority = settings.Value.Authority;
                options.MetadataAddress = settings.Value.MetadataAddress;
                options.Audience = settings.Value.Audience;
                options.RequireHttpsMetadata = new Uri(settings.Value.MetadataAddress).Scheme == "https";
                options.Backchannel = clients.CreateClient("jwt-backchannel");
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidIssuer = settings.Value.Authority;
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
            });
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole("driver", "operator").Build();
            options.AddPolicy("driver", policy => policy.RequireAuthenticatedUser().RequireRole("driver"));
            options.AddPolicy("operator", policy => policy.RequireAuthenticatedUser().RequireRole("operator"));
        });
        return builder;
    }
}
