using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Features.Charging;
using EvCharging.Api.Features.Drivers;
using EvCharging.Api.Features.Observations;
using EvCharging.Api.Features.Reservations;
using EvCharging.Api.Features.StationMedia;
using EvCharging.Api.Features.Stations;
using EvCharging.Api.Persistence;
using EvCharging.Contracts.Observations;
using EvCharging.DocumentStore;
using EvCharging.Messaging;
using EvCharging.ObservationStream;
using EvCharging.Persistence.Extensions;
using EvCharging.S3ObjectStore;
using Wolverine.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddApiAuthentication();
builder.AddObservationSourceAuthentication();
builder.AddHttpApiDefaults("/api");
builder.AddPlatformPersistence();
builder.AddObservationStreamProducer();
builder.AddObservationIngestion();
builder.AddDocumentStore();
builder.AddObjectStore();
builder.Services.AddOptions<ObservationClockSkewOptions>()
    .BindConfiguration(ObservationClockSkewOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.AddChargingProgress();
builder.AddMessaging(
    options =>
    {
        options.AddMessageStorage(builder.Configuration, "api");
        options.UseEntityFrameworkCoreTransactions();
        options.AddApplicationHandlers();
        options.AddReservationEventRouting();
        options.AddStationEventRouting();
        options.AddStationMediaRouting();
        options.AddChargingCommandRouting();
    });
builder.Services.AddApplicationServices();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplicationValidation();

var app = builder.Build();

app.UseHttpApiDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapHttpApiDiagnostics("api");
app.MapStations();
app.MapStationMedia();
app.MapDrivers();
app.MapReservations();
app.MapCharging();
app.MapChargingSessions();
app.MapObservations();

app.Run();
