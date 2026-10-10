using EvCharging.Gateway.Authentication;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace EvCharging.Gateway.Proxy;

internal static class ReverseProxyExtensions
{
    private const string ApiClusterId = "api";
    private const string ApiPathPrefix = "/api";
    private const string AnonymousPolicy = "anonymous";

    public static IHostApplicationBuilder AddGatewayReverseProxy(this IHostApplicationBuilder builder)
    {
        builder.Services.AddTransient<BrowserApiTransform>();
        builder.Services.AddReverseProxy()
            .AddTransforms(context =>
            {
                context.AddRequestTransform(BrowserApiTransform.StripBrowserHeaders);
                if (context.Route.AuthorizationPolicy == AnonymousPolicy)
                {
                    context.AddRequestHeaderRemove("Authorization");
                }
                else if (context.Route.AuthorizationPolicy == BrowserAuthenticationExtensions.BrowserSessionPolicy)
                {
                    context.RequestTransforms.Add(context.Services.GetRequiredService<BrowserApiTransform>());
                }
            })
            .AddServiceDiscoveryDestinationResolver()
            .LoadFromMemory(
                [
                    CreateApiRoute("platform-station-list", "/stations", AnonymousPolicy, methods: [HttpMethods.Get]),
                    CreateApiRoute(
                        "platform-station-detail", "/stations/{id:guid}", AnonymousPolicy, methods: [HttpMethods.Get]),
                    CreateApiRoute("platform-machine", "/internal/{**remainder}", authorizationPolicy: null),
                    CreateApiRoute("platform-callbacks", "/callbacks/{**remainder}", authorizationPolicy: null),
                    CreateApiRoute(
                        "platform-api",
                        "/{**remainder}",
                        BrowserAuthenticationExtensions.BrowserSessionPolicy,
                        order: 0)
                ],
                [new ClusterConfig
                {
                    ClusterId = ApiClusterId,
                    HealthCheck = new HealthCheckConfig
                    {
                        Active = new ActiveHealthCheckConfig
                        {
                            Enabled = true,
                            Interval = TimeSpan.FromSeconds(2),
                            Timeout = TimeSpan.FromSeconds(1),
                            Policy = "ConsecutiveFailures",
                            Path = "/health"
                        }
                    },
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        [ApiClusterId] = new() { Address = $"http://{ApiClusterId}/" }
                    }
                }]);

        return builder;
    }

    private static RouteConfig CreateApiRoute(
        string routeId,
        string path,
        string? authorizationPolicy,
        int order = -1,
        string[]? methods = null)
    {
        return new RouteConfig
        {
            RouteId = routeId,
            ClusterId = ApiClusterId,
            Order = order,
            AuthorizationPolicy = authorizationPolicy,
            Match = new RouteMatch { Path = $"{ApiPathPrefix}{path}", Methods = methods }
        }
            .WithTransformPathRemovePrefix(ApiPathPrefix);
    }
}
