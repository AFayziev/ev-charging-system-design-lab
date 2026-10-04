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
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["api"] = new() { Address = "https+http://api/" }
            }
        }]);

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => new { host = "gateway", stub = true });
app.MapReverseProxy();
app.Run();
