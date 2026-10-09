using System.Net.Http.Headers;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Yarp.ReverseProxy.Transforms;

namespace EvCharging.Gateway.Authentication;

internal sealed class BrowserApiTransform(IAntiforgery antiforgery) : RequestTransform
{
    public override async ValueTask ApplyAsync(RequestTransformContext context)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)
            && !HttpMethods.IsOptions(http.Request.Method) && !HttpMethods.IsTrace(http.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
        }

        var result = await http.GetUserAccessTokenAsync(ct: http.RequestAborted);
        if (!result.Succeeded)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.Token.AccessToken.ToString());
    }

    internal static ValueTask StripBrowserHeaders(RequestTransformContext context)
    {
        foreach (var header in context.ProxyRequest.Headers.Select(header => header.Key).ToArray())
        {
            if (header.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || header.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
                || header.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                || header.StartsWith("X-User-", StringComparison.OrdinalIgnoreCase)
                || header.StartsWith("X-Identity-", StringComparison.OrdinalIgnoreCase)
                || header.Equals("X-CSRF-TOKEN", StringComparison.OrdinalIgnoreCase))
            {
                context.ProxyRequest.Headers.Remove(header);
            }
        }

        return ValueTask.CompletedTask;
    }
}
