using EvCharging.ChargingWorker.Handlers;
using EvCharging.ChargingWorker.Observations;
using EvCharging.Contracts.Events.Sessions;
using EvCharging.Contracts.Observations;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Observations;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using EvCharging.Testing;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.Tracking;
using Xunit;

namespace EvCharging.ChargingWorker.Tests;

public sealed class SessionEvidenceBatchHandlerTests(PlatformDatabaseFixture fixture) :
    IClassFixture<PlatformDatabaseFixture>,
    IAsyncLifetime
{
    private const string IntegrationKey = "evidence-test";
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);
    private IHost? _messaging;

    public async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddDbContext<PlatformDbContext>(options => options.UseNpgsql(fixture.GetDataSource("charging_messaging")));
        builder.Services.AddSingleton<CompletionRecorder>();
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.UseEntityFrameworkCoreTransactions();
            options.PersistMessagesWithPostgresql(
                fixture.GetConnectionString("platform_migrator"),
                "charging_messages");
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.Policies.UseDurableLocalQueues();
            options.PublishMessage<SessionCompletedEvent>().ToLocalQueue("session-completed-tests");
            options.Discovery.IncludeType<CompleteChargingSessionMessageHandler>();
            options.Discovery.IncludeType<SessionCompletedRecorderHandler>();
        });
        _messaging = builder.Build();
        await _messaging.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_messaging is not null)
        {
            await _messaging.StopAsync();
            _messaging.Dispose();
        }
    }

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

    [Theory]
    [InlineData(SessionLifecycle.Stopping)]
    [InlineData(SessionLifecycle.Active)]
    public async Task Handle_FinalReading_CompletesTheSessionWithItsFrozenFacts(SessionLifecycle lifecycle)
    {
        var session = await SeedSessionAsync(lifecycle);

        await CompleteAsync(Reading(session, 10, 3m), Reading(session, 20, 7.25m, isFinal: true));

        var saved = await SavedSessionAsync(session);
        Assert.Equal(SessionLifecycle.Completed, saved.Lifecycle);
        Assert.Equal(7.25m, saved.FinalEnergyKWh);
        Assert.Equal(Start.AddSeconds(20), saved.CompletedAt);
        var completed = Assert.Single(Completions.For(session.Id));
        Assert.Equal(
            new SessionCompletedEvent(session.Id, session.ReservationId, 7.25m, Start.AddSeconds(20)), completed);
    }

    [Fact]
    public async Task Handle_FinalReadingDeliveredAgain_CompletesOnce()
    {
        var session = await SeedSessionAsync(SessionLifecycle.Stopping);
        var batch = new[] { Reading(session, 20, 7.25m, isFinal: true) };
        await CompleteAsync(batch);

        await CompleteAsync(batch);

        Assert.Single(Completions.For(session.Id));
        Assert.Equal(Start.AddSeconds(20), (await SavedSessionAsync(session)).CompletedAt);
    }

    [Fact]
    public async Task Handle_FinalEnergyWithoutTheStoppedState_DoesNotComplete()
    {
        var session = await SeedSessionAsync(SessionLifecycle.Stopping);
        var reading = Reading(session, 20, 7.25m) with { IsFinalEnergy = true };

        await CompleteAsync(reading);

        Assert.Equal(SessionLifecycle.Stopping, (await SavedSessionAsync(session)).Lifecycle);
        Assert.Empty(Completions.For(session.Id));
    }

    [Fact]
    public async Task Handle_FinalReadingForAStartingSession_DoesNotComplete()
    {
        var session = await SeedSessionAsync();

        await CompleteAsync(Reading(session, 20, 7.25m, isFinal: true));

        Assert.Equal(SessionLifecycle.Starting, (await SavedSessionAsync(session)).Lifecycle);
        Assert.Empty(Completions.For(session.Id));
    }

    [Fact]
    public async Task Handle_ContradictionAfterCompletion_KeepsTheFrozenFacts()
    {
        var session = await SeedSessionAsync(SessionLifecycle.Stopping);
        await CompleteAsync(Reading(session, 20, 7.25m, isFinal: true));

        await CompleteAsync(Reading(session, 25, 12m), Reading(session, 30, 9m, isFinal: true));

        var saved = await SavedSessionAsync(session);
        Assert.Equal(7.25m, saved.FinalEnergyKWh);
        Assert.Equal(Start.AddSeconds(20), saved.CompletedAt);
        Assert.Single(Completions.For(session.Id));
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

    private CompletionRecorder Completions => _messaging!.Services.GetRequiredService<CompletionRecorder>();

    private Task HandleAsync(params ObservationEnvelope[] envelopes) =>
        new SessionEvidenceBatchHandler(
            _messaging!.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ObservationClockSkewOptions { ClockSkew = TimeSpan.FromSeconds(5) }),
            NullLogger<SessionEvidenceBatchHandler>.Instance)
            .HandleAsync(envelopes, CancellationToken.None);

    // Waits for the completion job the batch enrolled, and for the event that job publishes.
    private async Task CompleteAsync(params ObservationEnvelope[] envelopes) =>
        await _messaging!.TrackActivity().Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(_ => HandleAsync(envelopes));

    private async Task<ChargingSession> SavedSessionAsync(ChargingSession session)
    {
        await using var database = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(fixture.GetDataSource("charging_messaging")).Options);
        return await database.Sessions.AsNoTracking().SingleAsync(value => value.Id == session.Id);
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

    private async Task<ChargingSession> SeedSessionAsync(SessionLifecycle lifecycle = SessionLifecycle.Starting)
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
        if (lifecycle is SessionLifecycle.Active or SessionLifecycle.Stopping)
        {
            operation.Session!.ConfirmStart(provider.Id, "provider-session", now);
        }

        if (lifecycle == SessionLifecycle.Stopping)
        {
            operation.Session!.AcceptStop();
        }


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

    public sealed class CompletionRecorder
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<SessionCompletedEvent> _events = new();

        public void Add(SessionCompletedEvent message) => _events.Enqueue(message);

        public SessionCompletedEvent[] For(Guid sessionId) => [.. _events.Where(value => value.SessionId == sessionId)];
    }

    public sealed class SessionCompletedRecorderHandler(CompletionRecorder recorder)
    {
        public void Handle(SessionCompletedEvent message) => recorder.Add(message);
    }
}
