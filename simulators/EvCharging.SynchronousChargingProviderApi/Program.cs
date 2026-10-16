using EvCharging.SynchronousChargingProviderApi.Authentication;
using EvCharging.SynchronousChargingProviderApi.Features.Commands;
using EvCharging.SynchronousChargingProviderApi.Features.Equipment;
using EvCharging.SynchronousChargingProviderApi.Features.Sessions;
using EvCharging.SynchronousChargingProviderApi.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddHttpApiDefaults();
builder.AddProviderPersistence();
builder.AddProviderAuthentication();
builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<StartChargingCommandHandler>();
builder.Services.AddScoped<GetCommandQueryHandler>();
builder.Services.AddScoped<GetCommandByReferenceQueryHandler>();
builder.Services.AddScoped<GetSessionQueryHandler>();
builder.Services.AddScoped<GetSessionByReferenceQueryHandler>();
builder.Services.AddScoped<GetConnectorQueryHandler>();
builder.Services.AddScoped<ConfigureConnectorCommandHandler>();

var app = builder.Build();

app.UseHttpApiDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapHttpApiDiagnostics("synchronous-charging-provider-api");
app.MapCommandEndpoints();
app.MapSessionEndpoints();
app.MapEquipmentEndpoints();

app.Run();
