using EvCharging.Api.Authentication;
using EvCharging.Api.Features.Stations;
using EvCharging.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddApiAuthentication();
builder.AddHttpApiDefaults("/api");
builder.AddPlatformPersistence();
builder.AddStations();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidation();

var app = builder.Build();

app.UseHttpApiDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapHttpApiDiagnostics("api");
app.MapStations();

app.Run();
