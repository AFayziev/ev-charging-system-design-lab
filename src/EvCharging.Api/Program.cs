using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Features.Drivers;
using EvCharging.Api.Features.Reservations;
using EvCharging.Api.Features.Stations;
using EvCharging.Api.Persistence;
using EvCharging.Persistence.Extensions;
using EvCharging.ServiceDefaults.Messaging;
using Wolverine.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddApiAuthentication();
builder.AddHttpApiDefaults("/api");
builder.AddPlatformPersistence();
builder.AddMessaging(
    options =>
    {
        options.AddMessageStorage(builder.Configuration, "api");
        options.UseEntityFrameworkCoreTransactions();
        options.AddApplicationHandlers();
        options.AddReservationEventRouting();
    });
builder.Services.AddApplicationServices();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidation();

var app = builder.Build();

app.UseHttpApiDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapHttpApiDiagnostics("api");
app.MapStations();
app.MapDrivers();
app.MapReservations();

app.Run();
