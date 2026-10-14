using System.Security.Claims;
using EvCharging.Gateway.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace EvCharging.Gateway.Tests;

public sealed class SignInFlowTests
{
    [Fact]
    public async Task CompleteSignIn_UnverifiedEmail_RedirectsToConfiguredPath()
    {
        const string FailurePath = "/account/sign-in-failed";
        using var host = CreateHost(FailurePath);
        var options = host.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext { RequestServices = host.Services };
        var scheme = new AuthenticationScheme(
            OpenIdConnectDefaults.AuthenticationScheme, null, typeof(OpenIdConnectHandler));
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("email", "synthetic@example.test"), new Claim("email_verified", "false")], "oidc")),
            new AuthenticationProperties(),
            scheme.Name);
        var context = new TicketReceivedContext(httpContext, scheme, options, ticket);

        await options.Events.TicketReceived(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal(StatusCodes.Status302Found, httpContext.Response.StatusCode);
        Assert.Equal(FailurePath, httpContext.Response.Headers.Location.ToString());
    }

    private static IHost CreateHost(string? failurePath)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["BrowserAuthentication:PublicOrigin"] = "https://app.example.test";
        builder.Configuration["BrowserAuthentication:SignInErrorPath"] = failurePath;
        builder.Configuration["BrowserAuthentication:Authority"] = "https://identity.example.test";
        builder.Configuration["BrowserAuthentication:MetadataAddress"] =
            "https://identity.example.test/.well-known/openid-configuration";
        builder.Configuration["BrowserAuthentication:ClientId"] = "synthetic-client";
        builder.Configuration["BrowserAuthentication:ClientSecret"] = "synthetic-secret";
        builder.Configuration["BrowserAuthentication:AllowLocalHttp"] = "false";
        builder.AddBrowserAuthentication();
        return builder.Build();
    }
}
