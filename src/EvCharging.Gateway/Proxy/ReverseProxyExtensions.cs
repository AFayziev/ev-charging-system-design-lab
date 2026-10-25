using System.ComponentModel.DataAnnotations;
using EvCharging.Gateway.Authentication;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace EvCharging.Gateway.Proxy;

internal static class ReverseProxyExtensions
{
    private const string ApiClusterId = "api";
    private const string SearchClusterId = "search-api";
    private const string ApiPathPrefix = "/api";
    private const string AnonymousPolicy = "anonymous";

    public static IHostApplicationBuilder AddGatewayReverseProxy(this IHostApplicationBuilder builder)
    {
        var searchOptions = new SearchRouteOptions();
        builder.Configuration.GetRequiredSection(SearchRouteOptions.SectionName).Bind(searchOptions);
        Validator.ValidateObject(searchOptions, new ValidationContext(searchOptions), validateAllProperties: true);
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
                    CreateRoute(
                        "platform-station-list",
                        "/stations",
                        AnonymousPolicy,
                        methods: [HttpMethods.Get]),
                    CreateRoute(
                        "platform-station-detail",
                        "/stations/{id:guid}",
                        AnonymousPolicy,
                        methods: [HttpMethods.Get]),
                    CreateRoute(
                        "platform-station-search",
                        "/search/stations",
                        AnonymousPolicy,
                        methods: [HttpMethods.Get],
                        clusterId: SearchClusterId),
                    CreateRoute(
                        "platform-machine",
                        "/internal/{**remainder}",
                        authorizationPolicy: null),
                    CreateRoute(
                        "platform-callbacks",
                        "/callbacks/{**remainder}",
                        authorizationPolicy: null),
                    CreateRoute(
                        "platform-api",
                        "/{**remainder}",
                        BrowserAuthenticationExtensions.BrowserSessionPolicy,
                        order: 0)
                ],
                [
                    CreateCluster(ApiClusterId),
                    CreateCluster(SearchClusterId, searchOptions.GatewayTimeout)
                ]);

        return builder;
    }

    private static RouteConfig CreateRoute(
        string routeId,
        string path,
        string? authorizationPolicy,
        int order = -1,
        string[]? methods = null,
        string clusterId = ApiClusterId)
    {
        return new RouteConfig
        {
            RouteId = routeId,
            ClusterId = clusterId,
            Order = order,
            AuthorizationPolicy = authorizationPolicy,
            Match = new RouteMatch { Path = $"{ApiPathPrefix}{path}", Methods = methods }
        }
            .WithTransformPathRemovePrefix(ApiPathPrefix);
    }

    // The search service is its own cluster, so its outage never marks the main API unhealthy, or the reverse.
    // A short request timeout makes a downed search service fail fast instead of holding callers for the default 100 s.
    private static ClusterConfig CreateCluster(string clusterId, TimeSpan? requestTimeout = null) => new()
    {
        ClusterId = clusterId,
        HttpRequest = requestTimeout is { } timeout ? new ForwarderRequestConfig { ActivityTimeout = timeout } : null,
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
            [clusterId] = new() { Address = $"http://{clusterId}/" }
        }
    };
}
