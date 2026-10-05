var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddHttpApiDefaults();
var app = builder.Build();
app.UseHttpApiDefaults();
app.MapHttpApiDiagnostics("notification-provider-api");
app.Run();
