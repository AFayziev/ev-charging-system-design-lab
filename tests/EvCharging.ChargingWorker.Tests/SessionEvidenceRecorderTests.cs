using EvCharging.ChargingWorker.Observations;
using EvCharging.Contracts.Observations;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Observations;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using EvCharging.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EvCharging.ChargingWorker.Tests;

public sealed class SessionEvidenceRecorderTests(PlatformDatabaseFixture fixture) :
    IClassFixture<PlatformDatabaseFixture>
{
    private const string IntegrationKey = "evidence-test";
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_FirstBatch_CreatesEvidence()
    {
        var session = await SeedSessionAsync();

        await HandleAsync(Reading(session, 0, 1m), Reading(session, 5, 2m));

        var evidence = await EvidenceAsync(session);
        Assert.Equal(Start.AddSeconds(5), evidence!.LastObservedAt);
        Assert.Null(evidence.FinalEnergyKWh);
    }

    [Fact]
    public async Task Handle_SameBatchAgain_LeavesTheRowUntouched()
    {
        var session = await SeedSessionAsync();
        var batch = new[] { Reading(session, 0, 1m), Reading(session, 5, 2m, isFinal: true) };
        await HandleAsync(batch);
        var first = await EvidenceAsync(session);

        await HandleAsync(batch);

        var second = await EvidenceAsync(session);
        Assert.Equal(first!.LastObservedAt, second!.LastObservedAt);
        Assert.Equal(first.FinalReadingAt, second.FinalReadingAt);
        // The row version only moves when the row is written.
        Assert.Equal(first.Version, second.Version);
    }

    [Fact]
    public async Task Handle_OlderReadingsWithNewSourceIds_LeaveLastObservedAt()
    {
        var session = await SeedSessionAsync();
        await HandleAsync(Reading(session, 30, 3m));

        await HandleAsync(Reading(session, 0, 1m), Reading(session, 10, 2m));

        Assert.Equal(Start.AddSeconds(30), (await EvidenceAsync(session))!.LastObservedAt);
    }

    [Fact]
    public async Task Handle_FinalReadingBeforeALaterReading_StillFillsTheFinalFacts()
    {
        var session = await SeedSessionAsync();
        await HandleAsync(Reading(session, 30, 9m));

        await HandleAsync(Reading(session, 10, 7.25m, isFinal: true));

        var evidence = await EvidenceAsync(session);
        Assert.Equal(7.25m, evidence!.FinalEnergyKWh);
        Assert.Equal(Start.AddSeconds(10), evidence.FinalReadingAt);
        Assert.Equal(Start.AddSeconds(30), evidence.LastObservedAt);
    }

    [Fact]
    public async Task Handle_ReadingDatedAheadOfItsReceipt_ChangesNothing()
    {
        var session = await SeedSessionAsync();

        await HandleAsync(Reading(session, 600, 1m, receivedSecondsAfterStart: 0));

        var evidence = await EvidenceAsync(session);
        Assert.Null(evidence!.LastObservedAt);
        Assert.Null(evidence.FinalEnergyKWh);
    }

    [Fact]
    public async Task Handle_UnknownSessionOrOtherIntegration_WritesNothing()
    {
        var session = await SeedSessionAsync();
        var unknown = Reading(session, 0, 1m) with { ExternalSessionId = Guid.NewGuid() };
        var otherIntegration = Reading(session, 0, 1m) with { IntegrationKey = "someone-else" };

        await HandleAsync(unknown, otherIntegration);

        Assert.Null(await EvidenceAsync(session));
    }

    [Fact]
    public async Task Handle_BatchSpanningTwoSessions_RecordsBoth()
    {
        var first = await SeedSessionAsync();
        var second = await SeedSessionAsync();

        await HandleAsync(Reading(first, 5, 1m), Reading(second, 20, 2m), Reading(first, 10, 3m));

        Assert.Equal(Start.AddSeconds(10), (await EvidenceAsync(first))!.LastObservedAt);
        Assert.Equal(Start.AddSeconds(20), (await EvidenceAsync(second))!.LastObservedAt);
    }

    [Fact]
    public async Task Handle_ConcurrentRecordersOfTheSameBatch_ConvergeOnOneRow()
    {
        var session = await SeedSessionAsync();
        var batch = new[] { Reading(session, 0, 1m), Reading(session, 5, 7m, isFinal: true) };

        // Both create the row; the loser fails and the consumer pipeline would retry it.
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => TryHandleAsync(batch)));
        if (results.Any(succeeded => !succeeded))
        {
            await HandleAsync(batch);
        }

        var evidence = await EvidenceAsync(session);
        Assert.Equal(Start.AddSeconds(5), evidence!.LastObservedAt);
        Assert.Equal(7m, evidence.FinalEnergyKWh);
    }

    private async Task<bool> TryHandleAsync(params ObservationEnvelope[] envelopes)
    {
        try
        {
            await HandleAsync(envelopes);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task HandleAsync(params ObservationEnvelope[] envelopes)
    {
        var services = new ServiceCollection();
        services.AddDbContext<PlatformDbContext>(
            options => options.UseNpgsql(fixture.GetDataSource("charging_messaging")));
        await using var provider = services.BuildServiceProvider();
        var recorder = new SessionEvidenceRecorder(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ObservationClockSkewOptions { ClockSkew = TimeSpan.FromSeconds(5) }),
            NullLogger<SessionEvidenceRecorder>.Instance);

        await recorder.HandleAsync(envelopes, CancellationToken.None);
    }

    private async Task<SessionEvidence?> EvidenceAsync(ChargingSession session)
    {
        await using var database = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(fixture.GetDataSource("charging_messaging")).Options);
        return await database.SessionEvidence.AsNoTracking()
            .SingleOrDefaultAsync(evidence => evidence.SessionId == session.Id);
    }

    private static ObservationEnvelope Reading(
        ChargingSession session,
        int secondsAfterStart,
        decimal energyKWh,
        bool isFinal = false,
        int? receivedSecondsAfterStart = null) => new(
            ObservationEnvelope.CurrentSchemaVersion,
            Guid.CreateVersion7(),
            IntegrationKey,
            Guid.NewGuid(),
            session.PublicId,
            "station",
            "charger",
            "connector",
            Start.AddSeconds(secondsAfterStart),
            Start.AddSeconds(receivedSecondsAfterStart ?? secondsAfterStart),
            isFinal ? ObservationState.Stopped : ObservationState.Charging,
            PowerKw: 11m,
            energyKWh,
            isFinal);

    private async Task<ChargingSession> SeedSessionAsync()
    {
        await using var database = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(fixture.GetDataSource("platform_writer")).Options);
        var station = new Station { Name = "Test", Address = "Test", TimeZoneId = "UTC", AccessInstructions = "Test" };
        var charger = new Charger { Station = station, Name = "Test" };
        var connector = new Connector { Charger = charger, ConnectorType = "Type2", PowerSupply = "AC" };
        var driver = Driver.Register(
            "https://test.invalid",
            Guid.NewGuid().ToString(),
            EmailAddress.Create("driver@example.test").Value);
        database.Connectors.Add(connector);
        database.Drivers.Add(driver);
        await database.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var reservation = Reservation.Book(
            connector,
            driver,
            now,
            now.AddHours(1),
            now,
            new(2, TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
        var operation = Operation.AcceptStart(ChargingSession.AcceptStart(reservation, now).Value, Guid.NewGuid());
        var provider = new ChargingProvider { Name = $"Evidence {Guid.NewGuid()}", IntegrationKey = IntegrationKey };
        database.Operations.Add(operation);
        database.ChargingProviders.Add(provider);
        await database.SaveChangesAsync();

        // The provider is told the session by its public identifier, which the database assigns on insert.
        database.Attempts.Add(new Attempt
        {
            OperationId = operation.Id,
            Number = 1,
            AuthorizedAt = now,
            ChargingProviderId = provider.Id,
            IntegrationKey = IntegrationKey,
            ProviderStationId = "station",
            ProviderChargerId = "charger",
            ProviderConnectorId = "connector",
            OperationPublicId = operation.PublicId,
            SessionPublicId = operation.Session!.PublicId
        });
        await database.SaveChangesAsync();
        return operation.Session;
    }
}
