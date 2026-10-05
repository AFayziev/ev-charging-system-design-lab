var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => new { host = "api", stub = true });
if (app.Environment.IsDevelopment())
{
    app.MapGet("/diagnostics/instance", () => new
    {
        host = "api",
        instance = $"{Environment.MachineName}:{Environment.ProcessId}",
        stub = true
    });
}
app.Run();
