using System.Net.Http.Headers;
using Duende.AccessTokenManagement;
using Duende.AccessTokenManagement.OpenIdConnect;
using EvCharging.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace EvCharging.Gateway.Authentication;

internal static class BrowserAuthenticationExtensions
{
    internal const string BrowserSessionPolicy = "browser-session";

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
            .Validate(
                options => RedirectHttpResult.IsLocalUrl(options.SignInErrorPath)
                    && options.SignInErrorPath.StartsWith('/'),
                "SignInErrorPath must be a root-relative local application path.")
            .ValidateOnStart();

        builder.Services.AddHttpClient("oidc-backchannel");
        builder.Services.AddHttpClient("driver-provisioning", client => client.BaseAddress = new Uri("http://api"));
        builder.Services.AddAuthentication(options =>
        {
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
                    context.HttpContext.RevokeRefreshTokenAsync(
                        new UserTokenRequestParameters
                        {
                            SignInScheme = Scheme.Parse(CookieAuthenticationDefaults.AuthenticationScheme)
                        },
                        ct: context.HttpContext.RequestAborted);
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
            .Configure<IOptions<BrowserAuthenticationOptions>, IHttpClientFactory, ILoggerFactory>(
                (options, settings, clients, loggers) =>
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
                    options.Events.OnTicketReceived = context => CompleteSignInAsync(
                        context,
                        clients,
                        loggers.CreateLogger(typeof(BrowserAuthenticationExtensions)),
                        configuration.SignInErrorPath);
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
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                BrowserSessionPolicy,
                policy =>
                {
                    policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme);
                    policy.RequireAuthenticatedUser();
                });
        });
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>()
            .Configure<IOptions<BrowserAuthenticationOptions>>((options, settings) =>
                options.Cookie.SecurePolicy = new Uri(settings.Value.PublicOrigin).Scheme == "http"
                    ? CookieSecurePolicy.None : CookieSecurePolicy.Always);
        return builder;
    }

    private static async Task CompleteSignInAsync(
        TicketReceivedContext context,
        IHttpClientFactory clients,
        ILogger logger,
        string signInErrorPath)
    {
        var principal = context.Principal;
        bool verified = bool.TryParse(principal?.FindFirst("email_verified")?.Value, out bool value) && value;
        if (!verified || string.IsNullOrWhiteSpace(principal?.FindFirst("email")?.Value))
        {
            RejectSignIn(context, signInErrorPath);
            return;
        }

        if (!principal.IsInRole(PlatformRole.Driver.ToClaimValue()))
        {
            return;
        }

        var accessToken = context.Properties?.GetTokenValue("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            RejectSignIn(context, signInErrorPath);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "drivers/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await clients.CreateClient("driver-provisioning")
                .SendAsync(request, context.HttpContext.RequestAborted);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            logger.LogWarning(
                "Sign-in rejected because driver profile provisioning returned HTTP {StatusCode}.",
                (int)response.StatusCode);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Sign-in rejected because driver profile provisioning failed.");
        }
        catch (OperationCanceledException exception) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Sign-in rejected because driver profile provisioning timed out.");
        }

        RejectSignIn(context, signInErrorPath);
    }

    private static void RejectSignIn(TicketReceivedContext context, string signInErrorPath)
    {
        context.HandleResponse();
        context.Response.Redirect(signInErrorPath);
    }
}
