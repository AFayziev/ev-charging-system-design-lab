using EvCharging.Gateway.Authentication;
using EvCharging.Gateway.Diagnostics;
using EvCharging.Gateway.Persistence;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddAuthenticationPersistence();
builder.AddAuthenticationSessions();
builder.AddBrowserAuthentication();
builder.AddHttpApiDefaults();
builder.AddFrontendDiagnostics();

builder.Services.AddTransient<BrowserApiTransform>();
builder.Services.AddReverseProxy()
    .AddTransforms(context =>
    {
        context.AddRequestTransform(BrowserApiTransform.StripBrowserHeaders);
        if (context.Route.RouteId == "platform-api")
        {
            context.RequestTransforms.Add(context.Services.GetRequiredService<BrowserApiTransform>());
        }
    })
    .AddServiceDiscoveryDestinationResolver()
    .LoadFromMemory(
        [new RouteConfig
        {
            RouteId = "platform-machine",
            ClusterId = "api",
            Order = -1,
            Match = new RouteMatch { Path = "/api/internal/{**remainder}" },
            Transforms = [new Dictionary<string, string> { ["PathRemovePrefix"] = "/api" }]
        }, new RouteConfig
        {
            RouteId = "platform-callbacks",
            ClusterId = "api",
            Order = -1,
            Match = new RouteMatch { Path = "/api/callbacks/{**remainder}" },
            Transforms = [new Dictionary<string, string> { ["PathRemovePrefix"] = "/api" }]
        }, new RouteConfig
        {
            RouteId = "platform-api",
            ClusterId = "api",
            Match = new RouteMatch { Path = "/api/{**remainder}" },
            Transforms = [new Dictionary<string, string> { ["PathRemovePrefix"] = "/api" }]
        }],
        [new ClusterConfig
        {
            ClusterId = "api",
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
                ["api"] = new() { Address = "http://api/" }
            }
        }]);

var app = builder.Build();

app.UseHttpApiDefaults();

if (app.Services.GetRequiredService<IOptions<FrontendDiagnosticsOptions>>().Value.Enabled)
{
    app.UseSentryTunneling("/diagnostics/sentry");
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Gateway-Instance"] = $"{Environment.MachineName}:{Environment.ProcessId}";
        await next(context);
    });
}

app.MapHttpApiDiagnostics("gateway");
app.MapBrowserAuthentication();
app.MapFrontendDiagnostics();
app.MapReverseProxy();

app.Run();
