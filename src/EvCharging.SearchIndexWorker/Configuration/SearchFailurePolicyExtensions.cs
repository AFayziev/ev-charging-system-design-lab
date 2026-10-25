using EvCharging.Persistence.Extensions;
using EvCharging.SearchIndexWorker.Indexing;
using Wolverine;
using Wolverine.ErrorHandling;

namespace EvCharging.SearchIndexWorker.Configuration;

internal static class SearchFailurePolicyExtensions
{
    // Unavailable dependencies retry for as long as it takes; they never exhaust into dead letters.
    public static WolverineOptions AddSearchFailurePolicies(this WolverineOptions options, TimeSpan[] retryDelays)
    {
        options.Policies.OnException(IsRetryable).ScheduleRetryIndefinitely(retryDelays);
        options.Policies
            .OnException(exception => exception is not OperationCanceledException && !IsRetryable(exception))
            .MoveToErrorQueue();
        return options;
    }

    private static bool IsRetryable(Exception exception) =>
        exception.IsTransientDatabaseFailure() || exception is SearchIndexUnavailableException;
}
