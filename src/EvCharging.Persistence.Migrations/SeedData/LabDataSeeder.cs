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

        var assignedStationIds = new List<long>();
        assignedStationIds.Add(await SeedStationAsync(
            context,
            "Synchronous Charging Provider",
            "north",
            "North Station",
            "1 Synthetic Avenue, Demo North Quarter, London",
            51.51m,
            -0.12m,
            "Type2",
            "AC",
            22m,
            1,
            2,
            "Fictional lab site. Enter the synthetic public car park from Synthetic Avenue; bays are beside the north exit.",
            cancellationToken));
        await SeedStationAsync(
            context,
            "Queued Charging Provider",
            "south",
            "South Station",
            "2 Synthetic Avenue, Demo South Quarter, London",
            51.49m,
            -0.11m,
            "CCS2",
            "DC",
            50m,
            1,
            2,
            "Fictional lab site. Use the south entrance off Synthetic Avenue; charging bays are beside the marked pedestrian route.",
            cancellationToken);

        assignedStationIds.Add(await SeedStationAsync(
            context,
            "Synchronous Charging Provider",
            "canal",
            "Demo Canal Yard",
            "14 Mock Canal Lane, Demo Wharf, London",
            51.5168m,
            -0.1284m,
            "Type2",
            "AC",
            11m,
            2,
            2,
            "Fictional lab site. Enter from Mock Canal Lane; four charging bays line the courtyard wall.",
            cancellationToken));
        assignedStationIds.Add(await SeedStationAsync(
            context,
            "Queued Charging Provider",
            "market",
            "Demo Market Square",
            "8 Example Market Road, Demo North Quarter, London",
            51.5126m,
            -0.1168m,
            "CCS2",
            "DC",
            150m,
            2,
            1,
            "Fictional lab site. Follow the signed car-park ramp from Example Market Road; chargers are on the upper deck.",
            cancellationToken));
        assignedStationIds.Add(await SeedStationAsync(
            context,
            "Synchronous Charging Provider",
            "library",
            "Demo Library Court",
            "27 Imaginary Library Street, Demo Wharf, London",
            51.5081m,
            -0.1331m,
            "Type2",
            "AC",
            7.4m,
            1,
            1,
            "Fictional lab site. The charging bay is in the side courtyard, reached from Imaginary Library Street.",
            cancellationToken));
        assignedStationIds.Add(await SeedStationAsync(
            context,
            "Queued Charging Provider",
            "riverside",
            "Demo Riverside Hub",
            "42 Mock River Walk, Demo South Quarter, London",
            51.4972m,
            -0.1062m,
            "CCS2",
            "DC",
            100m,
            3,
            2,
            "Fictional lab site. Enter the surface car park from Mock River Walk; six bays are along the east boundary.",
            cancellationToken));
        assignedStationIds.Add(await SeedStationAsync(
            context,
            "Synchronous Charging Provider",
            "garden",
            "Demo Garden Mews",
            "6 Sample Garden Mews, Demo South Quarter, London",
            51.4928m,
            -0.1214m,
            "Type2",
            "AC",
            22m,
            2,
            1,
            "Fictional lab site. Access the two bays through the car-park entrance on Sample Garden Mews.",
            cancellationToken));
        assignedStationIds.Add(await SeedStationAsync(
            context,
            "Queued Charging Provider",
            "exchange",
            "Demo Exchange Park",
            "19 Fictional Exchange Way, Demo North Quarter, London",
            51.5204m,
            -0.1108m,
            "CCS2",
            "DC",
            75m,
            1,
            2,
            "Fictional lab site. Enter from Fictional Exchange Way; chargers face the car-park exit lane.",
            cancellationToken));

        var existingAssignments = await context.OperatorAssignments
            .Where(assignment => assignment.OperatorUserId == operatorUser.Id)
            .Select(assignment => assignment.StationId)
            .ToListAsync(cancellationToken);
        var missingAssignments = assignedStationIds.Except(existingAssignments);
        foreach (long stationId in missingAssignments)
        {
            context.OperatorAssignments.Add(new() { OperatorUserId = operatorUser.Id, StationId = stationId });
        }

        await context.SaveChangesAsync(cancellationToken);
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
        int chargerCount,
        int evsesPerCharger,
        string accessInstructions,
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
            AccessInstructions = accessInstructions
        };
        context.Stations.Add(station);
        await context.SaveChangesAsync(cancellationToken);

        var providerStation = new ProviderStation
        {
            ChargingProviderId = provider.Id,
            StationId = station.Id,
            ExternalId = externalStationId
        };
        context.ProviderStations.Add(providerStation);
        await context.SaveChangesAsync(cancellationToken);

        for (var chargerNumber = 1; chargerNumber <= chargerCount; chargerNumber++)
        {
            var charger = new Charger { StationId = station.Id, Name = $"Charger {chargerNumber}" };
            context.Chargers.Add(charger);
            await context.SaveChangesAsync(cancellationToken);

            var providerCharger = new ProviderCharger
            {
                ProviderStationId = providerStation.Id,
                ChargerId = charger.Id,
                ExternalId = $"charger-{chargerNumber}"
            };
            context.ProviderChargers.Add(providerCharger);
            await context.SaveChangesAsync(cancellationToken);

            for (var number = 1; number <= evsesPerCharger; number++)
            {
                var evse = new Evse { ChargerId = charger.Id, Number = number };
                context.Evses.Add(evse);
                await context.SaveChangesAsync(cancellationToken);

                var connector = new Connector
                {
                    EvseId = evse.Id,
                    Number = 1,
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
        }

        await context.SaveChangesAsync(cancellationToken);
        return station.Id;
    }
}
