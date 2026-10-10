using EvCharging.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.Gateway.Authentication;

internal static class BrowserAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapBrowserAuthentication(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/auth/login",
            Results<BadRequest, ChallengeHttpResult> (string? returnUrl) =>
            {
                if (!TryReturnUrl(returnUrl, out var destination))
                {
                    return TypedResults.BadRequest();
                }

                return TypedResults.Challenge(
                    new AuthenticationProperties { RedirectUri = destination },
                    [OpenIdConnectDefaults.AuthenticationScheme]);
            });

        endpoints.MapGet(
            "/auth/session",
            async (HttpContext context, IAntiforgery antiforgery) =>
            {
                var authentication = await context.AuthenticateAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
                if (authentication.Succeeded)
                {
                    context.User = authentication.Principal!;
                }

                var tokens = antiforgery.GetAndStoreTokens(context);
                var signedIn = context.User.Identity?.IsAuthenticated == true;
                var user = signedIn ? new BrowserSessionUser(
                    context.User.FindFirst("name")?.Value,
                    context.User.FindFirst("preferred_username")?.Value,
                    context.User.FindFirst("email")?.Value,
                    Enum.GetValues<PlatformRole>()
                        .Where(role => context.User.IsInRole(role.ToClaimValue()))
                        .ToArray()) : null;

                return TypedResults.Ok(new BrowserSessionResponse(signedIn, user, tokens.RequestToken));
            });

        endpoints.MapPost(
            "/auth/logout",
            async Task<Results<BadRequest, SignOutHttpResult>>
            (HttpContext context, IAntiforgery antiforgery, string? returnUrl) =>
            {
                if (!TryReturnUrl(returnUrl, out var destination))
                {
                    return TypedResults.BadRequest();
                }

                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return TypedResults.BadRequest();
                }

                return TypedResults.SignOut(
                    new AuthenticationProperties { RedirectUri = destination },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
            })
            .RequireAuthorization(BrowserAuthenticationExtensions.BrowserSessionPolicy);
        return endpoints;
    }

    private static bool TryReturnUrl(string? returnUrl, out string destination)
    {
        destination = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
        return RedirectHttpResult.IsLocalUrl(destination);
    }
}
