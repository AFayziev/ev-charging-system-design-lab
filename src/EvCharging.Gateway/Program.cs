using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .AddServiceDiscoveryDestinationResolver()
    .LoadFromMemory(
        [new RouteConfig
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
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Gateway-Instance"] = $"{Environment.MachineName}:{Environment.ProcessId}";
        await next(context);
    });
}
app.MapDefaultEndpoints();
app.MapReverseProxy();
app.Run();
