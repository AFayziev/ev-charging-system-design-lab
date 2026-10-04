var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => new
{
    host = "notification-provider-api",
    role = "third-party-notification-service-simulator",
    stub = true,
    realDelivery = false
});
app.Run();
