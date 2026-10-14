using EvCharging.Persistence.Extensions;
using EvCharging.ServiceDefaults.Messaging;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;

namespace EvCharging.NotificationWorker.Configuration;

internal sealed class NotificationMessagingExtension(
    IOptions<MessagingFailureOptions> failures,
    IOptions<NotificationPreparationOptions> preparation) : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Policies.OnException(ExceptionExtensions.IsReservationNotificationConflict).RetryOnce();
        options.Policies.OnException(ExceptionExtensions.IsTransientDatabaseFailure)
            .ScheduleRetry(failures.Value.TransientDatabaseRetryDelays);
        options.Policies
            .OnException(exception => exception is not OperationCanceledException
                && !exception.IsTransientDatabaseFailure() && !exception.IsReservationNotificationConflict())
            .MoveToErrorQueue();
        options.ListenToRabbitQueue(ReservationEventTopology.NotificationQueue)
            .MaximumParallelMessages(preparation.Value.MaximumConcurrentPreparations);
    }
}
