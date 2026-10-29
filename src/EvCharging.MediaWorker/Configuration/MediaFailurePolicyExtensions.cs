using EvCharging.Persistence.Extensions;
using EvCharging.S3ObjectStore;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.ErrorHandling;

namespace EvCharging.MediaWorker.Configuration;

internal static class MediaFailurePolicyExtensions
{
    // Unavailable dependencies retry for as long as it takes; they never exhaust into dead letters.
    public static WolverineOptions AddMediaFailurePolicies(this WolverineOptions options, TimeSpan[] retryDelays)
    {
        options.Policies.OnException(IsRetryable).ScheduleRetryIndefinitely(retryDelays);
        options.Policies
            .OnException(exception => exception is not OperationCanceledException && !IsRetryable(exception))
            .MoveToErrorQueue();
        return options;
    }

    // A concurrency conflict means a gallery change got in between; the handler reads the new state on the retry.
    private static bool IsRetryable(Exception exception) =>
        exception.IsTransientDatabaseFailure() || exception.IsTransientStorageFailure()
        || exception is DbUpdateConcurrencyException;
}
