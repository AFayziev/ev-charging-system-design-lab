var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddHttpApiDefaults("/api");
var app = builder.Build();
app.UseHttpApiDefaults();
app.MapHttpApiDiagnostics("api");
app.Run();
