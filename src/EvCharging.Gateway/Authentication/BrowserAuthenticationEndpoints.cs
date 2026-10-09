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
        endpoints.MapGet("/auth/login", Results<BadRequest, ChallengeHttpResult> (string? returnUrl) =>
        {
            if (!TryReturnUrl(returnUrl, out var destination))
            {
                return TypedResults.BadRequest();
            }

            return TypedResults.Challenge(new AuthenticationProperties { RedirectUri = destination },
                [OpenIdConnectDefaults.AuthenticationScheme]);
        });

        endpoints.MapGet("/auth/session", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var tokens = antiforgery.GetAndStoreTokens(context);
            var signedIn = context.User.Identity?.IsAuthenticated == true;
            var display = signedIn ? new BrowserSessionDisplay(
                context.User.FindFirst("name")?.Value,
                context.User.FindFirst("preferred_username")?.Value,
                context.User.FindFirst("email")?.Value) : null;

            return TypedResults.Ok(new BrowserSessionResponse(signedIn, display, tokens.RequestToken));
        });

        endpoints.MapPost("/auth/logout", async Task<Results<BadRequest, SignOutHttpResult>>
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

            return TypedResults.SignOut(new AuthenticationProperties { RedirectUri = destination },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        });
        return endpoints;
    }

    private static bool TryReturnUrl(string? returnUrl, out string destination)
    {
        destination = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
        return RedirectHttpResult.IsLocalUrl(destination);
    }
}
