var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => new { host = "synchronous-charging-provider-api", stub = true });
app.Run();
