using EvCharging.Persistence.Extensions;
using Wolverine;
using Wolverine.ErrorHandling;

namespace EvCharging.ChargingWorker.Configuration;

public static class MessagingFailureExtensions
{
    // A transient database failure reschedules the message; any other failure, bar cancellation, parks it.
    public static void AddFailurePolicies(this WolverineOptions options, MessagingFailureOptions failureOptions)
    {
        options.Policies.OnException(ExceptionExtensions.IsTransientDatabaseFailure)
            .ScheduleRetry(failureOptions.TransientDatabaseRetryDelays);
        options.Policies
            .OnException(
                exception => exception is not OperationCanceledException && !exception.IsTransientDatabaseFailure())
            .MoveToErrorQueue();
    }
}
