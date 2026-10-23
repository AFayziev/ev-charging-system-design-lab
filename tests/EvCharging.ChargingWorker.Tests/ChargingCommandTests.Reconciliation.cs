using System.Net;
using EvCharging.ChargingWorker.Handlers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Xunit;

namespace EvCharging.ChargingWorker.Tests;

public sealed partial class ChargingCommandTests
{
    [Fact]
    public async Task FindUnreconciled_WindowMissedWhileUnresolved_ChecksAgainUntilEvidenceSettlesIt()
    {
        var operation = await PrepareAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, lost);
        // The checking window passes without a check.
        _clock.Advance(TimeSpan.FromMinutes(4));

        var check = Assert.IsType<CheckChargingStartMessage>(
            Assert.Single(await FindUnreconciledAsync(operation.Id)));

        Assert.True(check.Automatic);
        await using var database = Database();
        var extended = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        Assert.True(extended.AutomaticChecksEndAt > _clock.GetUtcNow());
        Assert.Empty(await FindUnreconciledAsync(operation.Id));
        using var queries = new ProviderTransport((_, _) => Task.FromResult(Applied(operation.PublicId)));
        using var host = CreateProviderHost(database, queries);
        await Checker(database, host).HandleAsync(check, CancellationToken.None);
        var settled = await database.Operations.AsNoTracking().Include(value => value.Session)
            .SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(CommandOutcome.Confirmed, settled.Outcome);
        Assert.Equal(SessionLifecycle.Active, settled.Session!.Lifecycle);
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Empty(await FindUnreconciledAsync(operation.Id));
    }

    [Fact]
    public async Task FindUnreconciled_ConcurrentScans_ScheduleOneCheck()
    {
        var operation = await PrepareAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, lost);
        _clock.Advance(TimeSpan.FromMinutes(4));

        var scans = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => FindUnreconciledAsync(operation.Id)));

        Assert.Single(scans.SelectMany(messages => messages));
    }

    [Fact]
    public async Task FindUnreconciled_SettledOperation_IsLeftAsItIs()
    {
        var operation = await PrepareAsync();
        using var applied = new ProviderTransport((_, _) => Task.FromResult(Applied(operation.PublicId)));
        await ExecuteAsync(operation.Id, applied);
        await using var database = Database();
        var settled = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        _clock.Advance(TimeSpan.FromMinutes(4));

        Assert.Empty(await FindUnreconciledAsync(operation.Id));

        var unchanged = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(settled.AutomaticChecksEndAt, unchanged.AutomaticChecksEndAt);
    }

    [Fact]
    public async Task FindUnreconciled_StartPastItsArrivalDeadline_IsLookedUpWithoutAnotherEffect()
    {
        var operation = await PrepareAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, lost);
        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.IsType<CheckChargingStartMessage>(Assert.Single(await FindUnreconciledAsync(operation.Id)));
        using var queries = new ProviderTransport(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var resend = Assert.Single(await CheckAsync(operation.Id, queries, automatic: true));

        // Authorization rejects the resend after the deadline.
        using var start = new ProviderTransport((_, _) => throw new InvalidOperationException("No call expected."));
        await ExecuteAsync(operation.Id, start, resend.AttemptNumber);
        Assert.Equal(0, start.Calls);
        await using var database = Database();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(1, saved.AuthorizedAttemptCount);
        Assert.Equal(CommandOutcome.Uncertain, saved.Outcome);
    }

    [Fact]
    public async Task FindUnreconciled_StopOnCompletedSession_SettlesOnlyTheStop()
    {
        var stop = await PrepareStopAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteStopAsync(stop.Id, lost);
        await CompleteSessionAsync(stop.SessionId);
        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.IsType<CheckChargingStopMessage>(Assert.Single(await FindUnreconciledAsync(stop.Id)));
        using var queries = new ProviderTransport(
            (_, _) => Task.FromResult(Rejected(stop.PublicId, "stop", "session_not_found")));

        Assert.Empty(await CheckStopAsync(stop.Id, queries));

        await using var database = Database();
        var saved = await database.Operations.AsNoTracking().Include(value => value.Session)
            .SingleAsync(value => value.Id == stop.Id);
        Assert.Equal(CommandOutcome.Rejected, saved.Outcome);
        Assert.Equal(SessionLifecycle.Completed, saved.Session!.Lifecycle);
    }

    [Fact]
    public async Task FindUnreconciled_MoreDueThanListenersRunAtOnce_ClaimsOnlyWhatRunsInTheWindow()
    {
        foreach (int _ in Enumerable.Range(0, 3))
        {
            var operation = await PrepareAsync();
            using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
            await ExecuteAsync(operation.Id, lost);
        }

        _clock.Advance(TimeSpan.FromMinutes(4));

        Assert.Equal(2, (await FindUnreconciledAsync(maximumConcurrentCommands: 2)).Count);
    }

    [Fact]
    public async Task FindUnreconciled_LongUnresolved_ChecksLessOftenUpToTheLimit()
    {
        var operation = await PrepareAsync();
        using var lost = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, lost);
        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Single(await FindUnreconciledAsync(operation.Id));

        await using var database = Database();
        var extended = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        var gap = extended.AutomaticChecksEndAt!.Value - _clock.GetUtcNow();
        Assert.InRange(gap, TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10));
    }

    // Other tests leave unresolved operations in the shared database, so the scan is not limited here.
    private async Task<object[]> FindUnreconciledAsync(Guid operationId) =>
        [.. (await FindUnreconciledAsync(maximumConcurrentCommands: 1000)).Where(message => message switch
        {
            CheckChargingStartMessage start => start.OperationId == operationId,
            CheckChargingStopMessage stop => stop.OperationId == operationId,
            _ => false
        })];

    private async Task<OutgoingMessages> FindUnreconciledAsync(int maximumConcurrentCommands)
    {
        await using var database = Database();
        var policy = TestExecutionOptions();
        policy.MaximumConcurrentCommands = maximumConcurrentCommands;
        return await FindUnreconciledChargingMessageHandler.HandleAsync(
            new(), database, _clock, Options.Create(policy), CancellationToken.None);
    }

    private async Task CompleteSessionAsync(Guid sessionId)
    {
        await using var database = WriterDatabase();
        var session = await database.Sessions.SingleAsync(value => value.Id == sessionId);
        Assert.True(session.Complete(7.25m, _clock.GetUtcNow()).IsSuccess);
        await database.SaveChangesAsync();
    }
}
