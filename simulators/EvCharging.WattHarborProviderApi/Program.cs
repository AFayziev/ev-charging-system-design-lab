using EvCharging.WattHarborProviderApi.Authentication;
using EvCharging.WattHarborProviderApi.Features.Commands;
using EvCharging.WattHarborProviderApi.Features.Equipment;
using EvCharging.WattHarborProviderApi.Features.Sessions;
using EvCharging.WattHarborProviderApi.Features.Simulation;
using EvCharging.WattHarborProviderApi.Features.Telemetry;
using EvCharging.WattHarborProviderApi.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddHttpApiDefaults();
builder.AddProviderPersistence();
builder.AddProviderAuthentication();
builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CommandFaults>();
builder.Services.AddScoped<StartChargingCommandHandler>();
builder.Services.AddScoped<StopChargingCommandHandler>();
builder.Services.AddScoped<GetCommandQueryHandler>();
builder.Services.AddScoped<GetCommandByExternalOperationIdQueryHandler>();
builder.Services.AddScoped<GetSessionQueryHandler>();
builder.Services.AddScoped<GetSessionByExternalSessionIdQueryHandler>();
builder.Services.AddScoped<GetConnectorQueryHandler>();
builder.Services.AddScoped<ConfigureConnectorCommandHandler>();
builder.Services.AddScoped<GetSessionSnapshotQueryHandler>();
builder.Services.AddScoped<ConfigureObservationCallbackCommandHandler>();
builder.Services.AddSingleton<TelemetryFaults>();
builder.AddObservationTelemetry();

var app = builder.Build();

app.UseHttpApiDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapHttpApiDiagnostics("wattharbor-provider-api");
var publicApi = app.MapGroup("")
    .AddEndpointFilterFactory(FaultInjectionFilters.ResponseLoss)
    .AddEndpointFilterFactory(FaultInjectionFilters.LookupFailure)
    .AddEndpointFilterFactory(FaultInjectionFilters.SnapshotFailure);
publicApi.MapCommandEndpoints();
publicApi.MapSessionEndpoints();
app.MapCommandFaultEndpoints();
app.MapTelemetryFaultEndpoints();
app.MapObservationCallbackEndpoints();
app.MapEquipmentEndpoints();

app.Run();
