using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Handlers;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Auditing;
using EvCharging.Persistence.Entities;
using EvCharging.Testing;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.Postgresql;
using Wolverine.Tracking;
using Xunit;

namespace EvCharging.ChargingWorker.Tests;

public sealed partial class ChargingCommandTests(PlatformDatabaseFixture fixture) :
    IClassFixture<PlatformDatabaseFixture>,
    IAsyncLifetime
{
    private const string IntegrationKey = "worker-test";
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private IHost? _messaging;
    private IServiceScope? _scope;

    public async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddDbContext<PlatformDbContext>(options => options.UseNpgsql(fixture.GetDataSource("charging_messaging")));
        builder.Services.AddSingleton<StartRecorder>();
        builder.Services.AddSingleton<StopRecorder>();
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.UseEntityFrameworkCoreTransactions();
            options.PersistMessagesWithPostgresql(
                fixture.GetConnectionString("platform_migrator"),
                "charging_messages");
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.Policies.UseDurableLocalQueues();
            options.PublishMessage<CheckChargingStart>().ToLocalQueue("automatic-check-tests");
            options.PublishMessage<StartCharging>().ToLocalQueue("resend-tests");
            options.PublishMessage<CheckChargingStop>().ToLocalQueue("automatic-stop-check-tests");
            options.PublishMessage<StopCharging>().ToLocalQueue("stop-resend-tests");
            options.Discovery.IncludeType<StartChargingRecorderHandler>();
            options.Discovery.IncludeType<StopChargingRecorderHandler>();
            options.Discovery.IncludeType<ExpireReservationHandler>();
        });
        _messaging = builder.Build();
        await _messaging.StartAsync();
        _scope = _messaging.Services.CreateScope();
    }

    public async Task DisposeAsync()
    {
        _scope?.Dispose();
        if (_messaging is not null)
        {
            await _messaging.StopAsync();
            _messaging.Dispose();
        }
    }

    [Fact]
    public async Task Start_UnconfiguredProvider_DoesNotAuthorizeAnEffect()
    {
        var operation = await PrepareAsync(integrationKey: null);
        using var transport = new ProviderTransport((_, _) => throw new InvalidOperationException("No call expected."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(operation.Id, transport));

        await using var database = Database();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(0, saved.AuthorizedAttemptCount);
        Assert.Equal(CommandOutcome.Pending, saved.Outcome);
        Assert.False(await database.Attempts.AnyAsync(value => value.OperationId == operation.Id));
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Start_ConcurrentDeliveries_CommitsOneAuthorizationBeforeHttpAndOneOutcome()
    {
        var operation = await PrepareAsync();
        using var transport = new ProviderTransport(async (request, cancellationToken) =>
        {
            await using var observed = Database();
            var attempt = await observed.Attempts.SingleAsync(
                value => value.OperationId == operation.Id, cancellationToken);
            var saved = await observed.Operations.SingleAsync(value => value.Id == operation.Id, cancellationToken);
            Assert.Equal(1, saved.AuthorizedAttemptCount);
            Assert.Equal(3, saved.RequestLimit);
            Assert.Equal(attempt.AuthorizedAt.AddMinutes(3), saved.AutomaticChecksEndAt);
            Assert.Equal(2, (await ScheduledChecksAsync(operation.Id)).Length);
            Assert.Null(attempt.FinishedAt);
            Assert.Equal(operation.PublicId.ToString(), request.Headers.GetValues("Idempotency-Key").Single());
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal(attempt.SessionPublicId, body.GetProperty("externalSessionId").GetGuid());
            Assert.Equal(attempt.ProviderConnectorId, body.GetProperty("connectorId").GetString());
            return Applied(operation.PublicId);
        });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ExecuteAsync(operation.Id, transport)));

        Assert.Equal(1, transport.Calls);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Confirmed, saved.Outcome);
        Assert.Equal(SessionLifecycle.Active, saved.Session!.Lifecycle);
        Assert.Equal(ReservationState.Consumed, saved.Session.Reservation!.State);
        var attempt = await database.Attempts.SingleAsync(value => value.OperationId == operation.Id);
        Assert.Equal(CommandOutcome.Confirmed, attempt.Outcome);
        Assert.Equal(IntegrationKey, attempt.IntegrationKey);
        Assert.Equal(attempt.ProviderSessionId.ToString(), saved.Session.ProviderSessionId);
        Assert.Equal(attempt.ProviderAppliedAt, saved.Session.StartedAt);
        Assert.Equal(
            3,
            await database.AuditEntries.CountAsync(value =>
                value.Action == AuditAction.Updated && (value.ResourceId == saved.Id
                    || value.ResourceId == saved.Session.Id || value.ResourceId == saved.Session.ReservationId)));
    }

    [Fact]
    public async Task Start_AuthorizationSaveFails_RollsBackAttemptAndScheduledChecks()
    {
        var operation = await PrepareAsync();
        using var transport = new ProviderTransport((_, _) => throw new InvalidOperationException("No call expected."));
        await using var database = Database(new InterruptAuthorizationSave());

        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(operation.Id, transport, database));

        await using var observed = Database();
        var saved = await observed.Operations.SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(0, saved.AuthorizedAttemptCount);
        Assert.Null(saved.AutomaticChecksEndAt);
        Assert.False(await observed.Attempts.AnyAsync(value => value.OperationId == operation.Id));
        Assert.Empty(await ScheduledChecksAsync(operation.Id));
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task CheckStart_AutomaticWindowEnds_StopsAutomaticCallsAndKeepsManualChecksAvailable()
    {
        var operation = await PrepareAsync();
        using var start = new ProviderTransport((_, _) => throw new HttpRequestException("Lost response."));
        await ExecuteAsync(operation.Id, start);
        await using var database = Database();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        var cutoff = saved.AutomaticChecksEndAt!.Value;
        using var queries = new ProviderTransport(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var host = CreateProviderHost(database, queries);
        var handler = Checker(database, host);

        await handler.HandleAsync(new(operation.Id, Automatic: true), CancellationToken.None);
        Assert.Equal(2, queries.Calls);
        _clock.Advance(cutoff - _clock.GetUtcNow());
        await handler.HandleAsync(new(operation.Id, Automatic: true), CancellationToken.None);
        Assert.Equal(2, queries.Calls);
        await handler.HandleAsync(new(operation.Id), CancellationToken.None);
        Assert.Equal(4, queries.Calls);
        saved = await database.Operations.AsNoTracking().Include(value => value.Session)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(cutoff, saved.AutomaticChecksEndAt);
        Assert.Equal(CommandOutcome.Uncertain, saved.Outcome);
        Assert.Equal(SessionLifecycle.Starting, saved.Session!.Lifecycle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckStart_LookupCanceled_PreservesClaimAndCallerCancellation(bool windowExpires)
    {
        var operation = await PrepareAsync();
        using var start = new ProviderTransport((_, _) => throw new HttpRequestException("Lost response."));
        await ExecuteAsync(operation.Id, start);
        await using var database = Database();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        var cutoff = saved.AutomaticChecksEndAt!.Value;
        using var caller = new CancellationTokenSource();
        using var queries = new ProviderTransport((_, cancellationToken) =>
        {
            if (windowExpires)
            {
                _clock.Advance(cutoff - _clock.GetUtcNow());
            }
            else
            {
                caller.Cancel();
            }

            Assert.True(cancellationToken.IsCancellationRequested);
            throw new OperationCanceledException(cancellationToken);
        });
        using var host = CreateProviderHost(database, queries);
        var handler = Checker(database, host);

        if (windowExpires)
        {
            Assert.Equal(
                ChargingCheckOutcome.Unresolved,
                (await handler.HandleAsync(new(operation.Id, Automatic: true), caller.Token)).Outcome);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handler.HandleAsync(new(operation.Id, Automatic: true), caller.Token));
        }

        Assert.Equal(1, queries.Calls);
        saved = await database.Operations.AsNoTracking().Include(value => value.Session)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Uncertain, saved.Outcome);
        Assert.Equal(SessionLifecycle.Starting, saved.Session!.Lifecycle);
        Assert.Equal(cutoff, saved.AutomaticChecksEndAt);
    }

    [Theory]
    [InlineData("transport")]
    [InlineData("unmatched")]
    [InlineData("interrupted")]
    [InlineData("server-error")]
    public async Task Start_UncertainOrInterruptedEffect_RetainsClaimAndNeverRepeatsOnRedelivery(string failure)
    {
        var operation = await PrepareAsync();
        using var cancellation = new CancellationTokenSource();
        using var transport = new ProviderTransport((_, _) =>
        {
            if (failure == "interrupted")
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            if (failure == "transport")
            {
                throw new HttpRequestException("The response was lost.");
            }

            if (failure == "server-error")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(Applied(Guid.NewGuid()));
        });
        if (failure == "interrupted")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ExecuteAsync(operation.Id, transport, cancellationToken: cancellation.Token));
        }
        else
        {
            await ExecuteAsync(operation.Id, transport);
        }

        _clock.Advance(TimeSpan.FromHours(2));
        await ExecuteAsync(operation.Id, transport);
        Assert.Equal(1, transport.Calls);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(failure == "interrupted" ? CommandOutcome.Pending : CommandOutcome.Uncertain, saved.Outcome);
        Assert.Equal(SessionLifecycle.Starting, saved.Session!.Lifecycle);
        Assert.Equal(ReservationState.Active, saved.Session.Reservation!.State);
        var attempt = await database.Attempts.SingleAsync(value => value.OperationId == operation.Id);
        Assert.Equal(1, saved.AuthorizedAttemptCount);
        Assert.Equal(failure == "interrupted", attempt.FinishedAt is null);
        if (failure == "unmatched")
        {
            Assert.NotNull(attempt.ResponseBody);
            Assert.Equal(200, attempt.ResponseStatusCode);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Start_RejectedEffect_ReleasesClaimAndAllowsAnotherLogicalStart(HttpStatusCode status)
    {
        var operation = await PrepareAsync();
        using var transport = new ProviderTransport((_, _) => Task
            .FromResult(new HttpResponseMessage(status)
            {
                Content = status == HttpStatusCode.Conflict ? JsonContent.Create(
                    new
                    {
                        status = (int)status,
                        code = "connector_unavailable",
                        commandId = Guid.NewGuid(),
                        externalOperationId = operation.PublicId
                    },
                    mediaType: new("application/problem+json")) : new StringContent("")
            }));
        await ExecuteAsync(operation.Id, transport);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Rejected, saved.Outcome);
        Assert.Equal(SessionLifecycle.Failed, saved.Session!.Lifecycle);
        Assert.Equal(ReservationState.Active, saved.Session.Reservation!.State);
        var attempt = await database.Attempts.SingleAsync(value => value.OperationId == operation.Id);
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Assert.Null(attempt.ProviderCommandId);
            Assert.Null(attempt.FailureCode);
            Assert.Equal((int)status, attempt.ResponseStatusCode);
        }
        else
        {
            Assert.NotNull(attempt.ProviderCommandId);
            Assert.Equal("connector_unavailable", attempt.FailureCode);
        }

        await ExecuteAsync(operation.Id, transport);
        Assert.Equal(1, transport.Calls);
        var next = Operation.AcceptStart(
            ChargingSession.AcceptStart(saved.Session.Reservation, _clock.GetUtcNow()).Value, Guid.NewGuid());
        await using (var acceptance = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(fixture.GetDataSource("platform_writer")).Options))
        {
            acceptance.Attach(saved.Session.Reservation);
            acceptance.Operations.Add(next);
            await acceptance.SaveChangesAsync();
        }

        using var applied = new ProviderTransport((_, _) => Task.FromResult(Applied(next.PublicId)));
        await ExecuteAsync(next.Id, applied);
        Assert.Equal(1, applied.Calls);
        Assert.Equal(
            CommandOutcome.Confirmed,
            (await database.Operations.AsNoTracking().SingleAsync(value => value.Id == next.Id)).Outcome);
    }

    [Fact]
    public async Task Start_DeadlinePassedBeforeAuthorization_RejectsWithoutEffectOrInlineExpiry()
    {
        var operation = await PrepareAsync();
        _clock.Advance(TimeSpan.FromMinutes(10));
        using var transport = new ProviderTransport((_, _) => throw new InvalidOperationException("No call expected."));
        await ExecuteAsync(operation.Id, transport);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Rejected, saved.Outcome);
        Assert.Equal(0, saved.AuthorizedAttemptCount);
        Assert.False(await database.Attempts.AnyAsync(value => value.OperationId == operation.Id));
        Assert.Equal(SessionLifecycle.Failed, saved.Session!.Lifecycle);
        Assert.Equal(ReservationState.Active, saved.Session.Reservation!.State);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Start_OutcomeCommitInterrupted_RollsBackOutcomeAndAuditsWithoutRepeatingEffect()
    {
        var operation = await PrepareAsync();
        using var transport = new ProviderTransport((_, _) => Task.FromResult(Applied(operation.PublicId)));
        await using (var database = Database(new InterruptOutcomeSave()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(operation.Id, transport, database));
        }

        await ExecuteAsync(operation.Id, transport);
        Assert.Equal(1, transport.Calls);
        await using var observed = Database();
        var saved = await observed.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Pending, saved.Outcome);
        Assert.Equal(SessionLifecycle.Starting, saved.Session!.Lifecycle);
        Assert.Equal(ReservationState.Active, saved.Session.Reservation!.State);
        Assert.Null((await observed.Attempts.SingleAsync(value => value.OperationId == operation.Id)).FinishedAt);
        Assert.False(await observed.AuditEntries.AnyAsync(value => value.Action == AuditAction.Updated
            && (value.ResourceId == saved.Id || value.ResourceId == saved.Session.Id
                || value.ResourceId == saved.Session.ReservationId)));
    }

    [Theory]
    [InlineData("command", CommandOutcome.Confirmed, ChargingCheckOutcome.Resolved)]
    [InlineData("rejected", CommandOutcome.Rejected, ChargingCheckOutcome.Resolved)]
    [InlineData("session", CommandOutcome.Confirmed, ChargingCheckOutcome.Resolved)]
    [InlineData("mismatched-session", CommandOutcome.Uncertain, ChargingCheckOutcome.Unavailable)]
    [InlineData("missing", CommandOutcome.Uncertain, ChargingCheckOutcome.Unresolved)]
    [InlineData("unauthorized", CommandOutcome.Uncertain, ChargingCheckOutcome.Unavailable)]
    [InlineData("forbidden", CommandOutcome.Uncertain, ChargingCheckOutcome.Unavailable)]
    public async Task CheckStart_QueriesExistingEffect_ResolvesOnlyMatchingEvidence(
        string evidence, CommandOutcome expected, ChargingCheckOutcome checkOutcome)
    {
        var operation = await PrepareAsync();
        using var lostResponse = new ProviderTransport((_, _) => throw new HttpRequestException("Response was lost."));
        await ExecuteAsync(operation.Id, lostResponse);
        await using var database = Database();
        var attempt = await database.Attempts.AsNoTracking().SingleAsync(value => value.OperationId == operation.Id);
        var providerSessionId = Guid.NewGuid();
        using var transport = new ProviderTransport((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            bool command = request.RequestUri!.AbsolutePath.Contains("commands/", StringComparison.Ordinal);
            Assert.EndsWith(
                (command ? attempt.OperationPublicId : attempt.SessionPublicId).ToString(),
                request.RequestUri.AbsolutePath,
                StringComparison.Ordinal);
            var rejected = new
            {
                id = Guid.NewGuid(),
                externalOperationId = operation.PublicId,
                kind = "start",
                outcome = "rejected",
                sessionId = (Guid?)null,
                appliedAt = (DateTimeOffset?)null,
                rejection = "connector_unavailable"
            };
            var session = new
            {
                id = providerSessionId,
                externalSessionId = attempt.SessionPublicId,
                stationId = attempt.ProviderStationId,
                chargerId = attempt.ProviderChargerId,
                connectorId = evidence == "session" ? attempt.ProviderConnectorId : "different",
                startedAt = _clock.GetUtcNow(),
                powerKw = 22m,
                state = "charging"
            };
            var response = evidence switch
            {
                "command" => Applied(operation.PublicId),
                "rejected" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(rejected) },
                "unauthorized" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                "forbidden" => new HttpResponseMessage(HttpStatusCode.Forbidden),
                "session" or "mismatched-session" when !command =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(session) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        });
        using var host = CreateProviderHost(database, transport);
        var handler = Checker(database, host);
        var result = await handler.HandleAsync(new(operation.Id), CancellationToken.None);
        Assert.Equal(checkOutcome, result.Outcome);
        var saved = await database.Operations.AsNoTracking().Include(value => value.Session!)
            .ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(expected, saved.Outcome);
        Assert.Equal(
            expected == CommandOutcome.Confirmed ? ReservationState.Consumed : ReservationState.Active,
            saved.Session!.Reservation!.State);
        var lifecycle = expected switch
        {
            CommandOutcome.Confirmed => SessionLifecycle.Active,
            CommandOutcome.Rejected => SessionLifecycle.Failed,
            _ => SessionLifecycle.Starting
        };
        Assert.Equal(lifecycle, saved.Session.Lifecycle);
        Assert.Equal(1, saved.AuthorizedAttemptCount);
        Assert.Equal(1, await database.Attempts.CountAsync(value => value.OperationId == operation.Id));
        int calls = transport.Calls;
        if (checkOutcome == ChargingCheckOutcome.Resolved)
        {
            await handler.HandleAsync(new(operation.Id), CancellationToken.None);
            Assert.Equal(calls, transport.Calls);
        }
    }

    [Fact]
    public async Task Start_LostResponse_RetryReusesKeyAndPayloadAndConfirmsWithoutSecondEffect()
    {
        var operation = await PrepareAsync();
        var requests = new List<(string Key, string Body)>();
        using var transport = new ProviderTransport(async (request, cancellationToken) =>
        {
            requests.Add((
                request.Headers.GetValues("Idempotency-Key").Single(),
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            return requests.Count == 1 ? throw new HttpRequestException("Lost.") : Applied(operation.PublicId);
        });

        await ExecuteAsync(operation.Id, transport);
        await ExecuteAsync(operation.Id, transport, attemptNumber: 2);

        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0], requests[1]);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Confirmed, saved.Outcome);
        Assert.Equal(SessionLifecycle.Active, saved.Session!.Lifecycle);
        Assert.Equal(2, saved.AuthorizedAttemptCount);
        var attempts = await database.Attempts.Where(value => value.OperationId == operation.Id)
            .OrderBy(value => value.Number)
            .ToListAsync();
        Assert.Equal([CommandOutcome.Uncertain, CommandOutcome.Confirmed], attempts.Select(value => value.Outcome));
        Assert.All(attempts, value => Assert.NotNull(value.FinishedAt));
        Assert.Equal(attempts[0].ProviderConnectorId, attempts[1].ProviderConnectorId);
        Assert.Equal(attempts[0].SessionPublicId, attempts[1].SessionPublicId);
        Assert.Equal(attempts[0].OperationPublicId, attempts[1].OperationPublicId);
    }

    [Fact]
    public async Task Start_RepeatedAmbiguity_StopsAtPersistedEffectLimitAcrossRedelivery()
    {
        var operation = await PrepareAsync();
        using var transport = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));

        foreach (int number in new[] { 1, 2, 2, 3, 3, 4 })
        {
            await ExecuteAsync(operation.Id, transport, attemptNumber: number);
        }

        Assert.Equal(3, transport.Calls);
        await using var database = Database();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Uncertain, saved.Outcome);
        Assert.Equal(3, saved.AuthorizedAttemptCount);
        Assert.Equal(3, saved.RequestLimit);
        Assert.Equal(3, await database.Attempts.CountAsync(value => value.OperationId == operation.Id));
    }

    [Fact]
    public async Task Start_RetryMessageOutOfOrderOrRedelivered_DoesNotSpendBudget()
    {
        var operation = await PrepareAsync();
        int calls = 0;
        using var transport = new ProviderTransport((_, _) => ++calls == 1
            ? throw new HttpRequestException("Lost.")
            : Task.FromResult(Applied(operation.PublicId)));
        await ExecuteAsync(operation.Id, transport);

        await ExecuteAsync(operation.Id, transport, attemptNumber: 3);
        Assert.Equal(1, transport.Calls);
        await ExecuteAsync(operation.Id, transport, attemptNumber: 2);
        await ExecuteAsync(operation.Id, transport, attemptNumber: 2);
        await ExecuteAsync(operation.Id, transport, attemptNumber: 3);

        Assert.Equal(2, transport.Calls);
        await using var database = Database();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Confirmed, saved.Outcome);
        Assert.Equal(2, saved.AuthorizedAttemptCount);
    }

    [Fact]
    public async Task Start_RetryAfterArrivalDeadline_AuthorizesNothingAndKeepsStartUncertain()
    {
        var operation = await PrepareAsync();
        using var transport = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, transport);

        _clock.Advance(TimeSpan.FromMinutes(10));
        await ExecuteAsync(operation.Id, transport, attemptNumber: 2);

        Assert.Equal(1, transport.Calls);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Uncertain, saved.Outcome);
        Assert.Equal(SessionLifecycle.Starting, saved.Session!.Lifecycle);
        Assert.Equal(ReservationState.Active, saved.Session.Reservation!.State);
        Assert.Equal(1, saved.AuthorizedAttemptCount);
        Assert.Equal(1, await database.Attempts.CountAsync(value => value.OperationId == operation.Id));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Start_RetryRejected_OnlyCodedRejectionSettlesTheStart(HttpStatusCode status)
    {
        var operation = await PrepareAsync();
        int calls = 0;
        using var transport = new ProviderTransport((_, _) =>
        {
            if (++calls == 1)
            {
                throw new HttpRequestException("Lost.");
            }

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = status == HttpStatusCode.Conflict ? JsonContent.Create(
                    new
                    {
                        status = (int)status,
                        code = "connector_unavailable",
                        commandId = Guid.NewGuid(),
                        externalOperationId = operation.PublicId
                    },
                    mediaType: new("application/problem+json")) : new StringContent("")
            });
        });
        await ExecuteAsync(operation.Id, transport);

        await ExecuteAsync(operation.Id, transport, attemptNumber: 2);

        Assert.Equal(2, transport.Calls);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        bool settled = status == HttpStatusCode.Conflict;
        Assert.Equal(settled ? CommandOutcome.Rejected : CommandOutcome.Uncertain, saved.Outcome);
        Assert.Equal(settled ? SessionLifecycle.Failed : SessionLifecycle.Starting, saved.Session!.Lifecycle);
        var retry = await database.Attempts
            .SingleAsync(value => value.OperationId == operation.Id && value.Number == 2);
        Assert.NotNull(retry.FinishedAt);
        Assert.Equal(settled ? null : (int)status, retry.ResponseStatusCode);
    }

    [Fact]
    public async Task Start_ResolvedByCheckBeforeRetry_DoesNotRepeatTheEffect()
    {
        var operation = await PrepareAsync();
        using var transport = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, transport);
        await using var database = Database();
        using var queries = new ProviderTransport((_, _) => Task.FromResult(Applied(operation.PublicId)));
        using var host = CreateProviderHost(database, queries);
        var checker = Checker(database, host);
        await checker.HandleAsync(new(operation.Id), CancellationToken.None);

        await ExecuteAsync(operation.Id, transport, attemptNumber: 2);

        Assert.Equal(1, transport.Calls);
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Confirmed, saved.Outcome);
        Assert.Equal(1, saved.AuthorizedAttemptCount);
    }

    [Fact]
    public async Task CheckStart_NoProviderRecord_ResendsTheSameOperationAsTheNextRequest()
    {
        var operation = await PrepareAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, lost);
        using var queries = new ProviderTransport(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var sent = await CheckAsync(operation.Id, queries, automatic: true);

        Assert.Equal([new StartCharging(operation.Id, 2)], sent);
        Assert.Equal(2, queries.Calls);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("lookup-unavailable")]
    [InlineData("limit-used")]
    [InlineData("denied-retry")]
    public async Task CheckStart_ResendNotPermitted_OnlyLooksUp(string scenario)
    {
        var operation = await PrepareAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, lost);
        if (scenario == "limit-used")
        {
            await ExecuteAsync(operation.Id, lost, attemptNumber: 2);
            await ExecuteAsync(operation.Id, lost, attemptNumber: 3);
        }

        if (scenario == "denied-retry")
        {
            using var denied = new ProviderTransport(
                (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
            await ExecuteAsync(operation.Id, denied, attemptNumber: 2);
        }

        using var queries = new ProviderTransport((_, _) => Task.FromResult(new HttpResponseMessage(
            scenario == "lookup-unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NotFound)));

        var sent = await CheckAsync(operation.Id, queries, automatic: scenario != "manual");

        Assert.Empty(sent);
        Assert.True(queries.Calls > 0);
    }

    [Fact]
    public async Task Expire_QueuedStartNeverAuthorized_RejectsItLocallyAndExpiresReservation()
    {
        var operation = await PrepareAsync();
        _clock.Advance(TimeSpan.FromMinutes(11));

        await ExpireAsync(operation.Id);

        using var transport = new ProviderTransport((_, _) => throw new InvalidOperationException("No call expected."));
        await ExecuteAsync(operation.Id, transport);
        Assert.Equal(0, transport.Calls);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Rejected, saved.Outcome);
        Assert.Equal(0, saved.AuthorizedAttemptCount);
        Assert.Equal(SessionLifecycle.Failed, saved.Session!.Lifecycle);
        Assert.Equal(ReservationState.Expired, saved.Session.Reservation!.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expire_AuthorizedStartUnresolved_KeepsClaimUntilALaterCheckSettlesIt(bool interrupted)
    {
        var operation = await PrepareAsync();
        using var cancellation = new CancellationTokenSource();
        using var start = new ProviderTransport((_, _) =>
        {
            if (!interrupted)
            {
                throw new HttpRequestException("Lost.");
            }

            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        if (interrupted)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ExecuteAsync(operation.Id, start, cancellationToken: cancellation.Token));
        }
        else
        {
            await ExecuteAsync(operation.Id, start);
        }

        _clock.Advance(TimeSpan.FromMinutes(11));

        await ExpireAsync(operation.Id);
        await using var database = Database();
        var retained = await database.Operations.AsNoTracking().Include(value => value.Session!)
            .ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(SessionLifecycle.Starting, retained.Session!.Lifecycle);
        Assert.Equal(ReservationState.Active, retained.Session.Reservation!.State);

        // The deadline only stops new requests; evidence of the existing one still settles the start.
        using var queries = new ProviderTransport((_, _) => Task.FromResult(Applied(operation.PublicId)));
        using var host = CreateProviderHost(database, queries);
        await Checker(database, host).HandleAsync(new(operation.Id), CancellationToken.None);
        var settled = await database.Operations.AsNoTracking().Include(value => value.Session!)
            .ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Confirmed, settled.Outcome);
        Assert.Equal(SessionLifecycle.Active, settled.Session!.Lifecycle);
        Assert.Equal(ReservationState.Consumed, settled.Session.Reservation!.State);
        Assert.Equal(1, settled.AuthorizedAttemptCount);
    }

    // Runs the worker's expiry handler; the reservation and session states show its outcome.
    private async Task ExpireAsync(Guid operationId)
    {
        await using var database = Database();
        var reservationId = await database.Operations.Where(value => value.Id == operationId)
            .Select(value => value.Session!.ReservationId)
            .SingleAsync();
        await _messaging!.MessageBus().InvokeAsync(new ExpireReservation(reservationId));
    }

    private async Task<StartCharging[]> CheckAsync(Guid operationId, ProviderTransport queries, bool automatic)
    {
        await using var database = Database();
        using var host = CreateProviderHost(database, queries);
        var messaging = _messaging!;
        await messaging.TrackActivity().Timeout(TimeSpan.FromSeconds(15)).ExecuteAndWaitAsync(
            bus => Checker(database, host, bus).HandleAsync(new(operationId, automatic), CancellationToken.None));
        return messaging.Services.GetRequiredService<StartRecorder>().For(operationId);
    }

    private CheckChargingStartCommandHandler Checker(PlatformDbContext database, IHost host, IMessageBus? bus = null) =>
        new(
            database,
            host.Services.GetRequiredService<ChargingProviderClientFactory>(),
            _clock,
            Options.Create(TestExecutionOptions()),
            bus ?? _scope!.ServiceProvider.GetRequiredService<IMessageBus>(),
            NullLogger<CheckChargingStartCommandHandler>.Instance);

    private static ChargingExecutionOptions TestExecutionOptions() => new()
    {
        MaximumCommandRequests = 3,
        MaximumConcurrentCommands = 2,
        MaximumAutomaticStatusChecks = 2,
        AutomaticStatusCheckInterval = TimeSpan.FromMinutes(1)
    };

    private async Task<T[]> ScheduledAsync<T>(Guid operationId, Func<T, Guid> operationOf)
    {
        var envelopes = await _messaging!.Services.GetRequiredService<IMessageStore>().Admin.AllIncomingAsync();
        return envelopes.Where(value => value.MessageType == typeof(T).FullName)
            .Select(value => JsonSerializer.Deserialize<T>(
                value.Data!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .Where(value => operationOf(value) == operationId)
            .ToArray();
    }

    private Task<CheckChargingStart[]> ScheduledChecksAsync(Guid operationId) =>
        ScheduledAsync<CheckChargingStart>(operationId, value => value.OperationId);

    private async Task ExecuteAsync(
        Guid operationId,
        ProviderTransport transport,
        int attemptNumber = 1,
        CancellationToken cancellationToken = default)
    {
        await using var database = Database();
        await ExecuteAsync(operationId, transport, database, attemptNumber, cancellationToken);
    }

    private async Task ExecuteAsync(
        Guid operationId,
        ProviderTransport transport,
        PlatformDbContext database,
        int attemptNumber = 1,
        CancellationToken cancellationToken = default)
    {
        using var host = CreateProviderHost(database, transport);
        using var scope = _messaging!.Services.CreateScope();
        var handler = new StartChargingCommandHandler(
            database,
            host.Services.GetRequiredService<ChargingProviderClientFactory>(),
            _clock,
            Options.Create(TestExecutionOptions()),
            scope.ServiceProvider.GetRequiredService<IDbContextOutbox>(),
            NullLogger<StartChargingCommandHandler>.Instance);
        await handler.HandleAsync(new(operationId, attemptNumber), cancellationToken);
    }

    private static IHost CreateProviderHost(PlatformDbContext database, ProviderTransport transport)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"{ChargingProviderIntegrationOptions.SectionName}:{IntegrationKey}:Client"] =
            ChargingProviderClientType.WattHarbor.ToString();
        builder.Services.AddOptions<ChargingProviderIntegrationOptions>(IntegrationKey)
            .Configure(value => value.Client = ChargingProviderClientType.WattHarbor);
        builder.Services.AddHttpClient(
            ChargingProviderClientFactory.HttpClientName(IntegrationKey),
            client => client.BaseAddress = new("https://provider.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => new ConnectionBoundaryHandler(database, transport));
        builder.Services.AddSingleton<ChargingProviderClientFactory>();
        return builder.Build();
    }

    private PlatformDbContext Database(params IInterceptor[] additional)
    {
        var writer = new AuditEntryWriter(new ServiceActor(), _clock);
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(fixture.GetDataSource("charging_messaging"))
            .AddInterceptors(new AuditSaveChangesInterceptor(writer))
            .AddInterceptors(additional).Options;
        return new(options);
    }

    private async Task<Operation> PrepareAsync(string? integrationKey = IntegrationKey)
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
        var now = _clock.GetUtcNow();
        var reservation = Reservation.Book(
            connector,
            driver,
            now,
            now.AddHours(1),
            now,
            new(2, TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
        var operation = Operation.AcceptStart(ChargingSession.AcceptStart(reservation, now).Value, Guid.NewGuid());
        database.Operations.Add(operation);
        var provider = new ChargingProvider
        {
            Name = $"Edited display {Guid.NewGuid()}",
            IntegrationKey = integrationKey
        };
        var providerStation = new ProviderStation
        {
            ChargingProvider = provider,
            Station = station,
            ExternalId = Guid.NewGuid().ToString()
        };
        var providerCharger = new ProviderCharger
        {
            ProviderStation = providerStation,
            Charger = charger,
            ExternalId = "charger"
        };
        database.ProviderConnectors.Add(new()
        {
            ProviderCharger = providerCharger,
            Connector = connector,
            ExternalId = "connector"
        });
        await database.SaveChangesAsync();
        return operation;
    }

    private HttpResponseMessage Applied(Guid externalOperationId, string kind = "start") => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(
            new
            {
                id = Guid.NewGuid(),
                externalOperationId,
                kind,
                outcome = "applied",
                sessionId = Guid.NewGuid(),
                appliedAt = _clock.GetUtcNow(),
                rejection = (string?)null
            })
    };

    public sealed class StartRecorder
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<StartCharging> _messages = new();

        public void Add(StartCharging message) => _messages.Enqueue(message);

        public StartCharging[] For(Guid operationId) =>
            [.. _messages.Where(value => value.OperationId == operationId)];
    }

    public sealed class StartChargingRecorderHandler(StartRecorder recorder)
    {
        public void Handle(StartCharging message) => recorder.Add(message);
    }

    private sealed class ProviderTransport(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return respond(request, cancellationToken);
        }
    }

    private sealed class ConnectionBoundaryHandler(PlatformDbContext database, ProviderTransport transport)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Null(database.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, database.Database.GetDbConnection().State);
            using var invoker = new HttpMessageInvoker(transport, disposeHandler: false);
            return await invoker.SendAsync(request, cancellationToken);
        }
    }

    private sealed class ServiceActor : IAuditActorAccessor
    {
        public AuditActor GetActor() => AuditActor.Service("charging-worker");
    }

    private sealed class InterruptOutcomeSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Operation>()
                .Any(value => value.Entity.Outcome == CommandOutcome.Confirmed) == true)
            {
                throw new InvalidOperationException("Interrupted before transaction commit.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class InterruptAuthorizationSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Attempt>().Any() == true)
            {
                throw new InvalidOperationException("Interrupted authorization save.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
