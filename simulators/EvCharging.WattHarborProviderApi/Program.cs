using EvCharging.WattHarborProviderApi.Authentication;
using EvCharging.WattHarborProviderApi.Features.Commands;
using EvCharging.WattHarborProviderApi.Features.Equipment;
using EvCharging.WattHarborProviderApi.Features.Sessions;
using EvCharging.WattHarborProviderApi.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddHttpApiDefaults();
builder.AddProviderPersistence();
builder.AddProviderAuthentication();
builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<StartChargingCommandHandler>();
builder.Services.AddScoped<GetCommandQueryHandler>();
builder.Services.AddScoped<GetCommandByExternalOperationIdQueryHandler>();
builder.Services.AddScoped<GetSessionQueryHandler>();
builder.Services.AddScoped<GetSessionByExternalSessionIdQueryHandler>();
builder.Services.AddScoped<GetConnectorQueryHandler>();
builder.Services.AddScoped<ConfigureConnectorCommandHandler>();

var app = builder.Build();

app.UseHttpApiDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapHttpApiDiagnostics("wattharbor-provider-api");
app.MapCommandEndpoints();
app.MapSessionEndpoints();
app.MapEquipmentEndpoints();

app.Run();
