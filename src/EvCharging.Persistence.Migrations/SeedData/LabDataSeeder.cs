using System.Text.Json;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Persistence.Migrations.SeedData;

public static class LabDataSeeder
{
    public static async Task SeedAsync(
        PlatformDbContext context,
        string identityIssuer,
        CancellationToken cancellationToken)
    {
        using var realm = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "SeedData", "evcharging-realm.json"), cancellationToken));
        var users = realm.RootElement.GetProperty("users");
        var driverSubject = SubjectForRole(users, "driver");
        var operatorSubject = SubjectForRole(users, "operator");

        if (!await context.Drivers.AnyAsync(
            driver => driver.IdentityIssuer == identityIssuer && driver.IdentitySubject == driverSubject,
            cancellationToken))
        {
            context.Drivers.Add(new() { IdentityIssuer = identityIssuer, IdentitySubject = driverSubject });
        }

        var operatorUser = await context.OperatorUsers.SingleOrDefaultAsync(
            user => user.IdentityIssuer == identityIssuer && user.IdentitySubject == operatorSubject,
            cancellationToken);
        if (operatorUser is null)
        {
            operatorUser = new() { IdentityIssuer = identityIssuer, IdentitySubject = operatorSubject };
            context.OperatorUsers.Add(operatorUser);
        }

        await context.SaveChangesAsync(cancellationToken);

        var assignedStationId = await SeedStationAsync(
            context,
            "Synchronous Charging Provider",
            "north",
            "North Station",
            "1 Synthetic Avenue",
            51.51m,
            -0.12m,
            "Type2",
            "AC",
            22m,
            cancellationToken);
        await SeedStationAsync(
            context,
            "Queued Charging Provider",
            "south",
            "South Station",
            "2 Synthetic Avenue",
            51.49m,
            -0.11m,
            "CCS2",
            "DC",
            50m,
            cancellationToken);

        if (!await context.OperatorAssignments.AnyAsync(
            assignment => assignment.OperatorUserId == operatorUser.Id && assignment.StationId == assignedStationId,
            cancellationToken))
        {
            context.OperatorAssignments.Add(new() { OperatorUserId = operatorUser.Id, StationId = assignedStationId });
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static string SubjectForRole(JsonElement users, string role) => users.EnumerateArray()
        .Single(user => user.GetProperty("realmRoles").EnumerateArray().Any(value => value.GetString() == role))
        .GetProperty("id")
        .GetString() ?? throw new InvalidOperationException($"The {role} seed user requires a subject ID.");

    private static async Task<long> SeedStationAsync(
        PlatformDbContext context,
        string providerName,
        string externalStationId,
        string name,
        string address,
        decimal latitude,
        decimal longitude,
        string connectorType,
        string powerSupply,
        decimal ratedPowerKW,
        CancellationToken cancellationToken)
    {
        var provider = await context.ChargingProviders.SingleOrDefaultAsync(
            value => value.Name == providerName, cancellationToken);
        if (provider is null)
        {
            provider = new() { Name = providerName };
            context.ChargingProviders.Add(provider);
            await context.SaveChangesAsync(cancellationToken);
        }

        var existing = await context.ProviderStations.SingleOrDefaultAsync(
            value => value.ChargingProviderId == provider.Id && value.ExternalId == externalStationId,
            cancellationToken);
        if (existing is not null)
        {
            return existing.StationId;
        }

        var station = new Station
        {
            Name = name,
            Address = address,
            Latitude = latitude,
            Longitude = longitude,
            TimeZoneId = "Europe/London",
            AccessInstructions = "Synthetic public car park; no access code required."
        };
        context.Stations.Add(station);
        await context.SaveChangesAsync(cancellationToken);

        var charger = new Charger { StationId = station.Id, Name = $"{name} Charger" };
        var providerStation = new ProviderStation
        {
            ChargingProviderId = provider.Id,
            StationId = station.Id,
            ExternalId = externalStationId
        };
        context.Chargers.Add(charger);
        context.ProviderStations.Add(providerStation);
        await context.SaveChangesAsync(cancellationToken);

        var providerCharger = new ProviderCharger
        {
            ProviderStationId = providerStation.Id,
            ChargerId = charger.Id,
            ExternalId = "charger-1"
        };
        context.ProviderChargers.Add(providerCharger);
        await context.SaveChangesAsync(cancellationToken);

        for (var number = 1; number <= 2; number++)
        {
            var evse = new Evse { ChargerId = charger.Id };
            context.Evses.Add(evse);
            await context.SaveChangesAsync(cancellationToken);

            var connector = new Connector
            {
                EvseId = evse.Id,
                ConnectorType = connectorType,
                PowerSupply = powerSupply,
                RatedPowerKW = ratedPowerKW
            };
            var providerEvse = new ProviderEvse
            {
                ProviderChargerId = providerCharger.Id,
                EvseId = evse.Id,
                ExternalId = $"evse-{number}"
            };
            context.Connectors.Add(connector);
            context.ProviderEvses.Add(providerEvse);
            await context.SaveChangesAsync(cancellationToken);

            context.ProviderConnectors.Add(new()
            {
                ProviderEvseId = providerEvse.Id,
                ConnectorId = connector.Id,
                ExternalId = "connector-1"
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        return station.Id;
    }
}
