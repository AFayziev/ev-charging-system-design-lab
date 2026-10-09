using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace EvCharging.Gateway.Authentication;

internal static class BrowserAuthenticationExtensions
{
    public static IHostApplicationBuilder AddBrowserAuthentication(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<BrowserAuthenticationOptions>()
            .Bind(builder.Configuration.GetSection("BrowserAuthentication"))
            .ValidateDataAnnotations()
            .Validate(
                options => Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var origin)
                    && origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0
                    && origin.UserInfo.Length == 0 && !options.PublicOrigin.EndsWith('/')
                    && (origin.Scheme == "https"
                        || (options.AllowLocalHttp == true && origin.Scheme == "http" && origin.IsLoopback)),
                "PublicOrigin must be an HTTPS origin, or an explicitly enabled loopback HTTP origin.")
            .Validate(
                options => Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority)
                    && authority.Query.Length == 0 && authority.Fragment.Length == 0 && authority.UserInfo.Length == 0
                    && (authority.Scheme == "https"
                        || (options.AllowLocalHttp == true && authority.Scheme == "http" && authority.IsLoopback)),
                "Authority must use HTTPS, or an explicitly enabled loopback HTTP identity provider.")
            .Validate(
                options => Uri.TryCreate(options.MetadataAddress, UriKind.Absolute, out var metadata)
                    && (metadata.Scheme is "https" or "http") && metadata.UserInfo.Length == 0
                    && metadata.Fragment.Length == 0,
                "MetadataAddress must be an explicit HTTP or HTTPS backchannel address.")
            .ValidateOnStart();

        builder.Services.AddHttpClient("oidc-backchannel");
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
            .AddCookie()
            .AddOpenIdConnect();

        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<BrowserAuthenticationOptions>, ITicketStore>((options, settings, tickets) =>
            {
                options.SessionStore = tickets;
                options.Cookie.Name = "EvCharging.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = new Uri(settings.Value.PublicOrigin).Scheme == "http"
                    ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
                options.Events.OnSigningOut = context =>
                    context.HttpContext.RevokeRefreshTokenAsync(ct: context.HttpContext.RequestAborted);
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<BrowserAuthenticationOptions>, IHttpClientFactory>((options, settings, clients) =>
            {
                var configuration = settings.Value;
                options.Authority = configuration.Authority;
                options.MetadataAddress = configuration.MetadataAddress;
                options.Backchannel = clients.CreateClient("oidc-backchannel");
                options.RequireHttpsMetadata = new Uri(configuration.MetadataAddress).Scheme == "https";
                options.ClientId = configuration.ClientId;
                options.ClientSecret = configuration.ClientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.SaveTokens = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
                options.TokenValidationParameters.ValidIssuer = configuration.Authority;
                options.CallbackPath = "/auth/callback";
                options.SignedOutCallbackPath = "/auth/logout-callback";
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = new Uri(configuration.PublicOrigin).Scheme == "http"
                    ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
                options.NonceCookie.SecurePolicy = options.CorrelationCookie.SecurePolicy;
                options.Scope.Add("email");
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.RedirectUri = configuration.PublicOrigin + options.CallbackPath;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                {
                    context.ProtocolMessage.PostLogoutRedirectUri =
                        configuration.PublicOrigin + options.SignedOutCallbackPath;
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddOpenIdConnectAccessTokenManagement();
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>()
            .Configure<IOptions<BrowserAuthenticationOptions>>((options, settings) =>
                options.Cookie.SecurePolicy = new Uri(settings.Value.PublicOrigin).Scheme == "http"
                    ? CookieSecurePolicy.None : CookieSecurePolicy.Always);
        return builder;
    }
}
