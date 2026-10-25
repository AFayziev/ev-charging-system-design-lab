using EvCharging.SearchApi.Features.Stations;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddHttpApiDefaults("/api");
builder.AddSearchIndexClient();
builder.Services.AddStationSearch();

var app = builder.Build();

app.UseHttpApiDefaults();

app.MapHttpApiDiagnostics("search-api");
app.MapStationSearch();

app.Run();
