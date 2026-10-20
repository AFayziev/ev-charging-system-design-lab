using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvCharging.ChargingWorker.Handlers;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;
using Wolverine.Tracking;
using Xunit;

namespace EvCharging.ChargingWorker.Tests;

public sealed partial class ChargingCommandTests
{
    [Fact]
    public async Task Stop_AppliedEffect_ConfirmsAndKeepsSessionStopping()
    {
        var stop = await PrepareStopAsync();
        using var transport = new ProviderTransport(async (request, cancellationToken) =>
        {
            await using var observed = Database();
            var attempt = await observed.Attempts.SingleAsync(value => value.OperationId == stop.Id, cancellationToken);
            var saved = await observed.Operations.SingleAsync(value => value.Id == stop.Id, cancellationToken);
            Assert.Equal(1, saved.AuthorizedAttemptCount);
            Assert.Equal(3, saved.RequestLimit);
            Assert.Equal(attempt.AuthorizedAt.AddMinutes(3), saved.AutomaticChecksEndAt);
            Assert.Equal(2, (await ScheduledAsync<CheckChargingStop>(stop.Id, value => value.OperationId)).Length);
            Assert.Null(attempt.FinishedAt);
            Assert.Equal("/commands/stop", request.RequestUri!.AbsolutePath);
            Assert.Equal(stop.PublicId.ToString(), request.Headers.GetValues("Idempotency-Key").Single());
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal(attempt.SessionPublicId, body.GetProperty("externalSessionId").GetGuid());
            return Applied(stop.PublicId, "stop");
        });

        await ExecuteStopAsync(stop.Id, transport);

        Assert.Equal(1, transport.Calls);
        await using var database = Database();
        var confirmed = await database.Operations.Include(value => value.Session!)
            .SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Confirmed, confirmed.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, confirmed.Session!.Lifecycle);
        var attempts = await database.Attempts.Where(value => database.Operations.Any(
            operation => operation.SessionId == confirmed.SessionId && value.OperationId == operation.Id))
            .ToListAsync();
        var start = attempts.Single(value => value.OperationId != stop.Id);
        var attempt = attempts.Single(value => value.OperationId == stop.Id);
        Assert.Equal(CommandOutcome.Confirmed, attempt.Outcome);
        Assert.NotNull(attempt.ProviderCommandId);
        Assert.Equal(start.IntegrationKey, attempt.IntegrationKey);
        Assert.Equal(start.ProviderConnectorId, attempt.ProviderConnectorId);
        Assert.Equal(confirmed.Session.ChargingProviderId, attempt.ChargingProviderId);
    }

    [Fact]
    public async Task Stop_RejectedEffect_ReturnsSessionToActive()
    {
        var stop = await PrepareStopAsync();
        using var transport = new ProviderTransport((_, _) => Task
            .FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(
                    new
                    {
                        status = (int)HttpStatusCode.Conflict,
                        code = "session_not_found",
                        commandId = Guid.NewGuid(),
                        externalOperationId = stop.PublicId
                    },
                    mediaType: new("application/problem+json"))
            }));

        await ExecuteStopAsync(stop.Id, transport);

        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!)
            .SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Rejected, saved.Outcome);
        Assert.Equal(SessionLifecycle.Active, saved.Session!.Lifecycle);
        Assert.Equal(
            "session_not_found",
            (await database.Attempts.SingleAsync(value => value.OperationId == stop.Id))
                .FailureCode);
    }

    [Fact]
    public async Task Stop_InitialAccessDenial_RejectsTheStopAndReturnsSessionToActive()
    {
        var stop = await PrepareStopAsync();
        using var transport = new ProviderTransport(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        await ExecuteStopAsync(stop.Id, transport);

        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!)
            .SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Rejected, saved.Outcome);
        Assert.Equal(SessionLifecycle.Active, saved.Session!.Lifecycle);
        Assert.Equal(
            (int)HttpStatusCode.Unauthorized,
            (await database.Attempts.SingleAsync(value => value.OperationId == stop.Id)).ResponseStatusCode);
    }

    [Fact]
    public async Task Stop_UncertainEffect_RetainsStoppingAndNeverRepeatsOnRedelivery()
    {
        var stop = await PrepareStopAsync();
        using var transport = new ProviderTransport(
            (_, _) => throw new HttpRequestException("The response was lost."));

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ExecuteStopAsync(stop.Id, transport)));

        Assert.Equal(1, transport.Calls);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!)
            .SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Uncertain, saved.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, saved.Session!.Lifecycle);
        var attempt = await database.Attempts.SingleAsync(value => value.OperationId == stop.Id);
        Assert.Equal(CommandExecution.TransportFailureCode, attempt.FailureCode);
        Assert.NotNull(attempt.FinishedAt);
    }

    [Fact]
    public async Task Stop_RetryMessageOutOfOrder_DoesNotSpendBudget()
    {
        var stop = await PrepareStopAsync();
        using var transport = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));

        await ExecuteStopAsync(stop.Id, transport, attemptNumber: 2);
        await ExecuteStopAsync(stop.Id, transport, attemptNumber: 3);
        await ExecuteStopAsync(stop.Id, transport);
        await ExecuteStopAsync(stop.Id, transport, attemptNumber: 3);

        Assert.Equal(1, transport.Calls);
        await using var database = Database();
        Assert.Equal(1, (await database.Operations.SingleAsync(value => value.Id == stop.Id)).AuthorizedAttemptCount);
    }

    [Fact]
    public async Task Stop_LostResponse_RetryReusesKeyAndConfirmsWithoutSecondEffect()
    {
        var stop = await PrepareStopAsync();
        var keys = new List<string>();
        using var transport = new ProviderTransport((request, _) =>
        {
            keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            return keys.Count == 1
                ? throw new HttpRequestException("The response was lost.")
                : Task.FromResult(Applied(stop.PublicId, "stop"));
        });

        await ExecuteStopAsync(stop.Id, transport);
        await ExecuteStopAsync(stop.Id, transport, attemptNumber: 2);

        Assert.Equal([stop.PublicId.ToString(), stop.PublicId.ToString()], keys);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!)
            .SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Confirmed, saved.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, saved.Session!.Lifecycle);
        Assert.Equal(2, saved.AuthorizedAttemptCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckStop_ProviderRecordsTheStop_ConfirmsWithoutAnotherRequest(bool interrupted)
    {
        var stop = await PrepareStopAsync();
        using var cancellation = new CancellationTokenSource();
        using var lost = new ProviderTransport((_, _) =>
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
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ExecuteStopAsync(stop.Id, lost, cancellationToken: cancellation.Token));
        }
        else
        {
            await ExecuteStopAsync(stop.Id, lost);
        }

        using var queries = new ProviderTransport((_, _) => Task.FromResult(Applied(stop.PublicId, "stop")));
        var sent = await CheckStopAsync(stop.Id, queries);

        Assert.Empty(sent);
        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!)
            .SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Confirmed, saved.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, saved.Session!.Lifecycle);
        Assert.Equal(1, saved.AuthorizedAttemptCount);
    }

    [Fact]
    public async Task CheckStop_ProviderRecordsARejection_ReturnsSessionToActive()
    {
        var stop = await PrepareStopAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteStopAsync(stop.Id, lost);
        using var queries = new ProviderTransport((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(
                new
                {
                    id = Guid.NewGuid(),
                    externalOperationId = stop.PublicId,
                    kind = "stop",
                    outcome = "rejected",
                    sessionId = (Guid?)null,
                    appliedAt = (DateTimeOffset?)null,
                    rejection = "session_not_found"
                })
        }));

        await CheckStopAsync(stop.Id, queries);

        await using var database = Database();
        var saved = await database.Operations.Include(value => value.Session!)
            .SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Rejected, saved.Outcome);
        Assert.Equal(SessionLifecycle.Active, saved.Session!.Lifecycle);
    }

    [Fact]
    public async Task CheckStop_NoProviderRecord_ResendsTheSameOperationAsTheNextRequest()
    {
        var stop = await PrepareStopAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteStopAsync(stop.Id, lost);
        using var queries = new ProviderTransport(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var sent = await CheckStopAsync(stop.Id, queries);

        Assert.Equal([new StopCharging(stop.Id, 2)], sent);
        Assert.Equal(1, queries.Calls);
    }

    [Theory]
    [InlineData("lookup-unavailable")]
    [InlineData("other-kind")]
    public async Task CheckStop_ResendNotPermitted_OnlyLooksUp(string scenario)
    {
        var stop = await PrepareStopAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteStopAsync(stop.Id, lost);
        using var queries = new ProviderTransport((_, _) => Task.FromResult(scenario switch
        {
            "lookup-unavailable" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "other-kind" => Applied(stop.PublicId, "start"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));

        var sent = await CheckStopAsync(stop.Id, queries);

        Assert.Empty(sent);
        Assert.Equal(1, queries.Calls);
        await using var database = Database();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Uncertain, saved.Outcome);
    }

    [Fact]
    public async Task Stop_OutcomeCommitInterrupted_IsResolvedByTheStopsOwnRecordWithoutRepeatingEffect()
    {
        var stop = await PrepareStopAsync();
        using var transport = new ProviderTransport((_, _) => Task.FromResult(Applied(stop.PublicId, "stop")));
        await using (var database = Database(new InterruptOutcomeSave()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ExecuteStopAsync(stop.Id, transport, database));
        }

        await ExecuteStopAsync(stop.Id, transport);
        Assert.Equal(1, transport.Calls);
        await using (var observed = Database())
        {
            Assert.Equal(
                CommandOutcome.Pending,
                (await observed.Operations.AsNoTracking().SingleAsync(value => value.Id == stop.Id)).Outcome);
        }

        using var queries = new ProviderTransport((_, _) => Task.FromResult(Applied(stop.PublicId, "stop")));
        Assert.Empty(await CheckStopAsync(stop.Id, queries));

        await using var settled = Database();
        var saved = await settled.Operations.Include(value => value.Session!)
            .SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Confirmed, saved.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, saved.Session!.Lifecycle);
        var attempt = await settled.Attempts.SingleAsync(value => value.OperationId == stop.Id);
        Assert.NotNull(attempt.FinishedAt);
        Assert.Equal(CommandOutcome.Confirmed, attempt.Outcome);
        Assert.Equal(CommandExecution.UnrecordedResponseCode, attempt.FailureCode);
    }

    [Fact]
    public async Task CheckStop_UnrecordedResponseWithoutProviderRecord_ClosesItAndResends()
    {
        var stop = await PrepareUnrecordedStopAsync();
        _clock.Advance(TestExecutionOptions().AutomaticStatusCheckInterval);
        using var queries = new ProviderTransport(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var sent = await CheckStopAsync(stop.Id, queries);

        Assert.Equal([new StopCharging(stop.Id, 2)], sent);
        await using var database = Database();
        var attempt = await database.Attempts.AsNoTracking().SingleAsync(value => value.OperationId == stop.Id);
        Assert.NotNull(attempt.FinishedAt);
        Assert.Equal(CommandExecution.UnrecordedResponseCode, attempt.FailureCode);
        Assert.Equal(
            CommandOutcome.Uncertain,
            (await database.Operations.AsNoTracking().SingleAsync(value => value.Id == stop.Id)).Outcome);
    }

    [Theory]
    [InlineData("lookup-unavailable")]
    [InlineData("young")]
    public async Task CheckStop_UnrecordedResponseWithoutEvidenceOfLoss_LeavesItUntouched(string scenario)
    {
        var stop = await PrepareUnrecordedStopAsync();
        if (scenario == "lookup-unavailable")
        {
            _clock.Advance(TestExecutionOptions().AutomaticStatusCheckInterval);
        }

        using var queries = new ProviderTransport((_, _) => Task.FromResult(new HttpResponseMessage(
            scenario == "young" ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable)));

        var sent = await CheckStopAsync(stop.Id, queries);

        Assert.Empty(sent);
        await using var database = Database();
        Assert.Null((await database.Attempts.AsNoTracking().SingleAsync(value => value.OperationId == stop.Id))
            .FinishedAt);
        Assert.Equal(
            CommandOutcome.Pending,
            (await database.Operations.AsNoTracking().SingleAsync(value => value.Id == stop.Id)).Outcome);
    }

    [Fact]
    public async Task CheckStop_EndedSessionWithoutTheStopsRecord_DoesNotConfirm()
    {
        var stop = await PrepareStopAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteStopAsync(stop.Id, lost);
        using var queries = new ProviderTransport((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.StartsWith("/commands", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Applied(stop.PublicId, "stop")));

        await CheckStopAsync(stop.Id, queries);

        Assert.Equal(1, queries.Calls);
        await using var database = Database();
        Assert.Equal(
            CommandOutcome.Uncertain,
            (await database.Operations.AsNoTracking().SingleAsync(value => value.Id == stop.Id)).Outcome);
    }

    /// <summary>A stop whose request was authorized but whose response was never recorded: pending, with an unfinished attempt.</summary>
    private async Task<Operation> PrepareUnrecordedStopAsync()
    {
        var stop = await PrepareStopAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteStopAsync(stop.Id, lost);
        await using var database = Database();
        await database.Attempts.Where(value => value.OperationId == stop.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.FinishedAt, (DateTimeOffset?)null)
                .SetProperty(value => value.FailureCode, (string?)null)
                .SetProperty(value => value.Outcome, (CommandOutcome?)null));
        await database.Operations.Where(value => value.Id == stop.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Outcome, CommandOutcome.Pending));
        return stop;
    }

    private async Task<Operation> PrepareStopAsync()
    {
        var start = await PrepareAsync();
        using var transport = new ProviderTransport((_, _) => Task.FromResult(Applied(start.PublicId)));
        await ExecuteAsync(start.Id, transport);
        await using var database = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(fixture.GetDataSource("platform_writer")).Options);
        var session = await database.Sessions.Include(value => value.Reservation)
            .SingleAsync(value => value.Id == start.SessionId);
        var stop = Operation.AcceptStop(session, Guid.NewGuid(), _clock.GetUtcNow()).Value;
        database.Operations.Add(stop);
        await database.SaveChangesAsync();
        return stop;
    }

    private async Task ExecuteStopAsync(
        Guid operationId,
        ProviderTransport transport,
        int attemptNumber = 1,
        CancellationToken cancellationToken = default)
    {
        await using var database = Database();
        await ExecuteStopAsync(operationId, transport, database, attemptNumber, cancellationToken);
    }

    private async Task ExecuteStopAsync(
        Guid operationId,
        ProviderTransport transport,
        PlatformDbContext database,
        int attemptNumber = 1,
        CancellationToken cancellationToken = default)
    {
        using var host = CreateProviderHost(database, transport);
        using var scope = _messaging!.Services.CreateScope();
        var handler = new StopChargingCommandHandler(
            database,
            host.Services.GetRequiredService<ChargingProviderClientFactory>(),
            _clock,
            Options.Create(TestExecutionOptions()),
            scope.ServiceProvider.GetRequiredService<IDbContextOutbox>(),
            NullLogger<StopChargingCommandHandler>.Instance);
        await handler.HandleAsync(new(operationId, attemptNumber), cancellationToken);
    }

    private async Task<StopCharging[]> CheckStopAsync(Guid operationId, ProviderTransport queries)
    {
        await using var database = Database();
        using var host = CreateProviderHost(database, queries);
        var messaging = _messaging!;
        await messaging.TrackActivity().Timeout(TimeSpan.FromSeconds(15)).ExecuteAndWaitAsync(
            bus => new CheckChargingStopCommandHandler(
                database,
                host.Services.GetRequiredService<ChargingProviderClientFactory>(),
                _clock,
                Options.Create(TestExecutionOptions()),
                bus,
                NullLogger<CheckChargingStopCommandHandler>.Instance)
                .HandleAsync(new(operationId), CancellationToken.None));
        return messaging.Services.GetRequiredService<StopRecorder>().For(operationId);
    }

    public sealed class StopRecorder
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<StopCharging> _messages = new();

        public void Add(StopCharging message) => _messages.Enqueue(message);

        public StopCharging[] For(Guid operationId) =>
            [.. _messages.Where(value => value.OperationId == operationId)];
    }

    public sealed class StopChargingRecorderHandler(StopRecorder recorder)
    {
        public void Handle(StopCharging message) => recorder.Add(message);
    }
}
