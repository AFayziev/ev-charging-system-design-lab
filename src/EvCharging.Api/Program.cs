using EvCharging.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddApiAuthentication();
builder.AddHttpApiDefaults("/api");

var app = builder.Build();

app.UseHttpApiDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapHttpApiDiagnostics("api");

app.Run();
