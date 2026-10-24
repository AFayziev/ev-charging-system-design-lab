using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EvCharging.ChargingWorker.Tests;

public sealed partial class ChargingCommandTests
{
    [Theory]
    [InlineData("applied", CommandOutcome.Confirmed, SessionLifecycle.Active, ReservationState.Consumed)]
    [InlineData("lost", CommandOutcome.Uncertain, SessionLifecycle.Starting, ReservationState.Active)]
    public async Task Start_ExpiryRunsWhileTheProviderCallIsInFlight_SettlesFromTheOutcomeAfterTheDeadline(
        string response, CommandOutcome outcome, SessionLifecycle lifecycle, ReservationState state)
    {
        var operation = await PrepareAsync();
        using var transport = ExpiringTransport(operation, response);

        await ExecuteAsync(operation.Id, transport);

        Assert.Equal(1, transport.Calls);
        await using var database = Database();
        var saved = await LoadAsync(database, operation.Id);
        Assert.Equal(outcome, saved.Outcome);
        Assert.Equal(lifecycle, saved.Session!.Lifecycle);
        Assert.Equal(state, saved.Session.Reservation!.State);
        Assert.Equal(1, saved.AuthorizedAttemptCount);
    }

    [Fact]
    public async Task Start_RejectedWhileExpiryRuns_FreesTheClaimAndLeavesTheReservationToTheNextScan()
    {
        var operation = await PrepareAsync();
        using var transport = ExpiringTransport(operation, "rejected");

        await ExecuteAsync(operation.Id, transport);

        await using var database = Database();
        var rejected = await LoadAsync(database, operation.Id);
        Assert.Equal(CommandOutcome.Rejected, rejected.Outcome);
        Assert.Equal(SessionLifecycle.Failed, rejected.Session!.Lifecycle);
        Assert.Equal(ReservationState.Active, rejected.Session.Reservation!.State);
        await ExpireAsync(operation.Id);
        var expired = await LoadAsync(database, operation.Id);
        Assert.Equal(ReservationState.Expired, expired.Session!.Reservation!.State);
    }

    [Fact]
    public async Task Start_ConcurrentRetryDeliveries_SpendEachAttemptNumberOnce()
    {
        var operation = await PrepareAsync();
        using var transport = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, transport);

        // A later number runs only once the one before it has finished, so how many of these deliveries run varies.
        await Task.WhenAll(new[] { 2, 3, 2, 3, 4, 4 }.Select(number =>
            ExecuteAsync(operation.Id, transport, attemptNumber: number)));

        await using var database = Database();
        var numbers = await database.Attempts.Where(value => value.OperationId == operation.Id)
            .Select(value => value.Number)
            .ToListAsync();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        Assert.InRange(transport.Calls, 2, 3);
        Assert.Equal(transport.Calls, numbers.Count);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.Equal(transport.Calls, saved.AuthorizedAttemptCount);
    }

    [Fact]
    public async Task Start_LimitRaisedAfterTheFirstRequest_KeepsTheLimitPersistedOnTheOperation()
    {
        var operation = await PrepareAsync();
        using var transport = new ProviderTransport((_, _) => throw new HttpRequestException("Lost."));
        await ExecuteAsync(operation.Id, transport);
        var raised = TestExecutionOptions(maximumCommandRequests: 10);

        foreach (int number in new[] { 2, 3, 4 })
        {
            await ExecuteAsync(operation.Id, transport, attemptNumber: number, options: raised);
        }

        Assert.Equal(3, transport.Calls);
        await using var database = Database();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        Assert.Equal(3, saved.RequestLimit);
        Assert.Equal(3, saved.AuthorizedAttemptCount);
    }

    // A provider whose answer arrives after the arrival deadline, with the expiry scan having run in between.
    private ProviderTransport ExpiringTransport(Operation operation, string response) =>
        new(async (_, _) =>
        {
            _clock.Advance(TimeSpan.FromMinutes(11));
            await ExpireAsync(operation.Id);
            return response switch
            {
                "applied" => Applied(operation.PublicId),
                "rejected" => CodedRejection(operation.PublicId),
                _ => throw new HttpRequestException("Lost.")
            };
        });

    private static Task<Operation> LoadAsync(PlatformDbContext database, Guid operationId) =>
        database.Operations.AsNoTracking().Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operationId);
}
