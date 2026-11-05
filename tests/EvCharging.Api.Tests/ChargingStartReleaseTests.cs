using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Charging;
using EvCharging.Api.Features.Stations;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Observations;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Auditing;
using EvCharging.Persistence.Entities;
using EvCharging.Testing;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class ChargingStartReleaseTests(PlatformDatabaseFixture fixture) :
    IClassFixture<PlatformDatabaseFixture>,
    IAsyncLifetime
{
    private static readonly DateTimeOffset SlotStart = new(2030, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastRequestedAt = SlotStart.AddMinutes(2);
    private static readonly TimeSpan ReleaseWindow = TimeSpan.FromMinutes(10);
    // Each test starts just before the release window ends.
    private readonly FakeTimeProvider _clock = new(LastRequestedAt + ReleaseWindow - TimeSpan.FromSeconds(1));
    private ApiHandlerHost? _api;

    public async Task InitializeAsync() =>
        _api = await ApiHandlerHost.StartAsync<ReleaseChargingStartCommandHandler>(
            fixture,
            _clock,
            services =>
            {
                services.AddAuthorization();
                services.AddScoped<IAuthorizationHandler, StationAssignmentAuthorizationHandler>();
                services.Configure<ChargingStartReleaseOptions>(options => options.ReleaseWindow = ReleaseWindow);
            },
            options =>
            {
                options.Discovery.IncludeType<ListUnsettledOperationsQueryHandler>();
                options.Discovery.IncludeType<StartChargingCommandHandler>();
            });

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
    }

    [Fact]
    public async Task Release_UncertainStartPastItsArrivalDeadline_FreesTheConnectorAndTheRestOfTheSlot()
    {
        var (chargerId, operatorIdentity) = await SeedAssignedChargerAsync();
        var (start, connector) = await SeedStartAsync(chargerId, CommandOutcome.Uncertain);
        PassReleaseWindow();

        var result = await ReleaseAsync(operatorIdentity, start.PublicId);

        Assert.True(result.IsSuccess);
        await using var database = fixture.CreateContext();
        var operation = await database.Operations.Include(value => value.Session)
            .SingleAsync(value => value.Id == start.Id);
        Assert.Equal(CommandOutcome.Uncertain, operation.Outcome);
        Assert.Equal(SessionLifecycle.Failed, operation.Session!.Lifecycle);
        Assert.Equal(_clock.GetUtcNow(), operation.ReleasedAt);
        Assert.Null(operation.AutomaticChecksEndAt);
        var reservation = await database.Reservations.SingleAsync(value => value.Id == operation.Session.ReservationId);
        Assert.Equal(ReservationState.Expired, reservation.State);
        Assert.Equal(_clock.GetUtcNow(), reservation.ExpiredAt);
        var audit = Assert.Single(
            await database.AuditEntries
                .Where(entry => entry.ResourceType == AuditResourceType.Operation && entry.ResourceId == start.Id
                    && entry.Action == AuditAction.Updated)
                .ToListAsync());
        Assert.Equal(AuditActorType.User, audit.ActorType);
        Assert.Equal(operatorIdentity.Subject, audit.ActorSubject);

        // Another driver can book the rest of the released slot and start on the same connector.
        var (driver, driverIdentity) = await fixture.SeedDriverAsync();
        var now = _clock.GetUtcNow();
        var next = await fixture.SeedReservationAsync(connector, driver, now, SlotStart.AddMinutes(30), now);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var started = await _api!.InvokeAsync<Result<StartOperationResponse>>(
            driverIdentity, new StartChargingCommand(next.PublicId, Guid.NewGuid()));
        Assert.True(started.IsSuccess);
    }

    [Fact]
    public async Task Release_RepeatedAfterRelease_KeepsTheFirstRelease()
    {
        var (chargerId, operatorIdentity) = await SeedAssignedChargerAsync();
        var (start, _) = await SeedStartAsync(chargerId, CommandOutcome.Uncertain);
        PassReleaseWindow();
        var first = await ReleaseAsync(operatorIdentity, start.PublicId);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var repeated = await ReleaseAsync(operatorIdentity, start.PublicId);

        Assert.Equal(first.Value.ReleasedAt, repeated.Value.ReleasedAt);
    }

    [Theory]
    [InlineData("before window")]
    [InlineData("observed")]
    [InlineData("settled first")]
    public async Task Release_NotReleasable_IsRefusedAndLeavesTheStartUnchanged(string scenario)
    {
        var (chargerId, operatorIdentity) = await SeedAssignedChargerAsync();
        var (start, _) = await SeedStartAsync(
            chargerId, scenario == "settled first" ? CommandOutcome.Confirmed : CommandOutcome.Uncertain);
        if (scenario != "before window")
        {
            PassReleaseWindow();
        }

        if (scenario == "observed")
        {
            await SeedObservationAsync(start.SessionId);
        }

        var result = await ReleaseAsync(operatorIdentity, start.PublicId);

        Assert.Equal(
            ChargingStartError.StartNotReleasableCode,
            Assert.IsType<ChargingStartError>(Assert.Single(result.Errors)).Code);
        await using var database = fixture.CreateContext();
        var operation = await database.Operations.Include(value => value.Session)
            .SingleAsync(value => value.Id == start.Id);
        Assert.Null(operation.ReleasedAt);
        Assert.NotEqual(SessionLifecycle.Failed, operation.Session!.Lifecycle);
    }

    [Fact]
    public async Task Release_StartOnAnUnassignedStation_IsForbidden()
    {
        var (chargerId, _) = await SeedAssignedChargerAsync();
        var (_, otherOperator) = await fixture.SeedAssignedStationAsync();
        var (start, _) = await SeedStartAsync(chargerId, CommandOutcome.Uncertain);
        PassReleaseWindow();

        var result = await ReleaseAsync(otherOperator, start.PublicId);

        Assert.IsType<ForbiddenError>(Assert.Single(result.Errors));
        await using var database = fixture.CreateContext();
        Assert.Null(await database.Operations.Where(value => value.Id == start.Id)
            .Select(value => value.ReleasedAt)
            .SingleAsync());
    }

    [Fact]
    public async Task ListUnsettled_ShowsOnlyAssignedUnreleasedOperationsWithTheirAttempts()
    {
        var (chargerId, operatorIdentity) = await SeedAssignedChargerAsync();
        var (otherChargerId, _) = await SeedAssignedChargerAsync();
        var (releasable, releasableConnector) = await SeedStartAsync(chargerId, CommandOutcome.Uncertain);
        var (observed, _) = await SeedStartAsync(chargerId, CommandOutcome.Uncertain);
        await SeedObservationAsync(observed.SessionId);
        var (confirmed, _) = await SeedStartAsync(chargerId, CommandOutcome.Confirmed);
        var (released, _) = await SeedStartAsync(chargerId, CommandOutcome.Uncertain);
        PassReleaseWindow();
        Assert.True((await ReleaseAsync(operatorIdentity, released.PublicId)).IsSuccess);
        var (unassigned, _) = await SeedStartAsync(otherChargerId, CommandOutcome.Uncertain);

        var result = await _api!.InvokeAsync<Result<UnsettledOperationListResponse>>(
            operatorIdentity, new ListUnsettledOperationsRequest());

        var listed = result.Value.Operations.ToDictionary(operation => operation.Id);
        Assert.Equal(
            new[] { releasable.PublicId, observed.PublicId }.Order(),
            listed.Keys.Order());
        Assert.DoesNotContain(confirmed.PublicId, listed.Keys);
        Assert.DoesNotContain(released.PublicId, listed.Keys);
        Assert.DoesNotContain(unassigned.PublicId, listed.Keys);

        var item = listed[releasable.PublicId];
        Assert.True(item.CanRelease);
        Assert.False(item.HasObservations);
        Assert.Equal(LastRequestedAt, item.LastRequestedAt);
        Assert.Equal(LastRequestedAt + ReleaseWindow, item.ReleasableAt);
        Assert.Equal(releasableConnector.Number, item.Equipment.ConnectorNumber);
        var attempt = Assert.Single(item.Attempts);
        Assert.Equal("test-integration", attempt.IntegrationKey);
        Assert.Equal("provider-connector", attempt.ProviderConnectorId);
        Assert.Equal(CommandOutcome.Uncertain, attempt.Outcome);

        Assert.False(listed[observed.PublicId].CanRelease);
        Assert.True(listed[observed.PublicId].HasObservations);
    }

    [Fact]
    public async Task ListUnsettled_CursorReleasedSinceThePreviousPage_ContinuesAfterIt()
    {
        var (chargerId, operatorIdentity) = await SeedAssignedChargerAsync();
        await SeedStartAsync(chargerId, CommandOutcome.Uncertain);
        await SeedStartAsync(chargerId, CommandOutcome.Uncertain);
        PassReleaseWindow();
        var firstPage = await _api!.InvokeAsync<Result<UnsettledOperationListResponse>>(
            operatorIdentity, new ListUnsettledOperationsRequest(Limit: 1));
        var cursor = Assert.Single(firstPage.Value.Operations).Id;
        Assert.True((await ReleaseAsync(operatorIdentity, cursor)).IsSuccess);

        var secondPage = await _api!.InvokeAsync<Result<UnsettledOperationListResponse>>(
            operatorIdentity, new ListUnsettledOperationsRequest(firstPage.Value.NextAfterId, Limit: 1));

        Assert.Equal(cursor, firstPage.Value.NextAfterId);
        Assert.NotEqual(cursor, Assert.Single(secondPage.Value.Operations).Id);
    }

    [Fact]
    public async Task ListUnsettled_BeforeTheWindow_ShowsWhenTheStartBecomesReleasable()
    {
        var (chargerId, operatorIdentity) = await SeedAssignedChargerAsync();
        var (start, _) = await SeedStartAsync(chargerId, CommandOutcome.Uncertain);

        var result = await _api!.InvokeAsync<Result<UnsettledOperationListResponse>>(
            operatorIdentity, new ListUnsettledOperationsRequest());

        var item = Assert.Single(result.Value.Operations, operation => operation.Id == start.PublicId);
        Assert.False(item.CanRelease);
        Assert.Equal(LastRequestedAt + ReleaseWindow, item.ReleasableAt);
    }

    private void PassReleaseWindow() => _clock.SetUtcNow(LastRequestedAt + ReleaseWindow);

    private Task<Result<ReleasedChargingStartResponse>> ReleaseAsync(UserIdentity identity, Guid operationId) =>
        _api!.InvokeAsync<Result<ReleasedChargingStartResponse>>(
            identity, new ReleaseChargingStartCommand(operationId));

    private async Task<(Guid ChargerId, UserIdentity Operator)> SeedAssignedChargerAsync()
    {
        var (station, identity) = await fixture.SeedAssignedStationAsync();
        await using var database = fixture.CreateContext();
        var chargerId = await database.Chargers.Where(value => value.StationId == station.Id)
            .Select(value => value.Id)
            .SingleAsync();
        return (chargerId, identity);
    }

    // Each start holds its own connector, as the connector claim allows only one at a time.
    private async Task<Connector> SeedConnectorAsync(Guid chargerId)
    {
        await using var database = fixture.CreateContext();
        int number = await database.Connectors.CountAsync(value => value.ChargerId == chargerId) + 1;
        var connector = new Connector
        {
            ChargerId = chargerId,
            Number = number,
            ConnectorType = "Type2",
            PowerSupply = "AC",
            RatedPowerKW = 22
        };
        database.Connectors.Add(connector);
        await database.SaveChangesAsync();
        return connector;
    }

    // A start the worker authorized once: either its answer was lost, or a later check settled it.
    private async Task<(Operation Start, Connector Connector)> SeedStartAsync(Guid chargerId, CommandOutcome outcome)
    {
        var connector = await SeedConnectorAsync(chargerId);
        var (driver, _) = await fixture.SeedDriverAsync();
        var reservation = await fixture.SeedReservationAsync(
            connector, driver, SlotStart, SlotStart.AddMinutes(30), SlotStart.AddMinutes(-1));
        await using var database = fixture.CreateContext();
        database.Attach(reservation);
        var operation = Operation.AcceptStart(
            ChargingSession.AcceptStart(reservation, SlotStart.AddMinutes(1)).Value, Guid.NewGuid());
        Assert.True(operation.MarkUncertain().IsSuccess);
        if (outcome == CommandOutcome.Confirmed)
        {
            var providerId = await SeedChargingProviderAsync();
            Assert.True(operation.ConfirmStart(providerId, "provider-session", LastRequestedAt).IsSuccess);
        }

        database.Operations.Add(operation);
        await database.SaveChangesAsync();
        await database.Operations.Where(value => value.Id == operation.Id).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(value => value.AuthorizedAttemptCount, 1)
                .SetProperty(value => value.RequestLimit, 3));
        database.Attempts.Add(new Attempt
        {
            OperationId = operation.Id,
            Number = 1,
            AuthorizedAt = LastRequestedAt,
            FinishedAt = LastRequestedAt.AddSeconds(3),
            Outcome = CommandOutcome.Uncertain,
            FailureCode = "provider_transport_failure",
            ChargingProviderId = Guid.NewGuid(),
            IntegrationKey = "test-integration",
            ProviderStationId = "provider-station",
            ProviderChargerId = "provider-charger",
            ProviderConnectorId = "provider-connector",
            OperationPublicId = operation.PublicId,
            SessionPublicId = operation.Session!.PublicId
        });
        await database.SaveChangesAsync();
        return (operation, connector);
    }

    private async Task<Guid> SeedChargingProviderAsync()
    {
        await using var database = fixture.CreateContext("platform_migrator");
        var provider = new ChargingProvider { Name = "Test provider" };
        database.ChargingProviders.Add(provider);
        await database.SaveChangesAsync();
        return provider.Id;
    }

    // Observations are recorded by the charging worker, under its own role.
    private async Task SeedObservationAsync(Guid sessionId)
    {
        await using var database = fixture.CreateContext("charging_messaging");
        var evidence = SessionEvidence.Create(sessionId);
        evidence.Record(
            LastRequestedAt.AddMinutes(1),
            LastRequestedAt.AddMinutes(1),
            energyKWh: 0m,
            isFinal: false,
            clockSkew: TimeSpan.FromSeconds(5));
        database.SessionEvidence.Add(evidence);
        await database.SaveChangesAsync();
    }
}
