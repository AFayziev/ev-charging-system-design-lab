using EvCharging.Messaging;
using EvCharging.NotificationWorker.Delivery;
using EvCharging.Persistence.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;

namespace EvCharging.NotificationWorker.Configuration;

internal sealed class NotificationMessagingExtension(
    IOptions<MessagingFailureOptions> failures,
    IOptions<NotificationPreparationOptions> preparation,
    IOptions<NotificationDeliveryOptions> delivery) : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Policies.OnException(ExceptionExtensions.IsReservationNotificationConflict).RetryOnce();
        options.Policies.OnException(IsDeliveryConflict).RetryOnce();
        var notCaptured = options.Policies.OnException<NotificationNotCapturedException>();
        if (delivery.Value.RetryDelays.Length > 0)
        {
            notCaptured.ScheduleRetry(delivery.Value.RetryDelays);
        }

        notCaptured.MoveToErrorQueue();
        options.Policies.OnException(ExceptionExtensions.IsTransientDatabaseFailure)
            .ScheduleRetry(failures.Value.TransientDatabaseRetryDelays);
        options.Policies
            .OnException(exception => exception is not OperationCanceledException
                && !exception.IsTransientDatabaseFailure() && !exception.IsReservationNotificationConflict()
                && !IsDeliveryConflict(exception) && exception is not NotificationNotCapturedException)
            .MoveToErrorQueue();
        options.ListenToRabbitQueue(ReservationEventTopology.NotificationQueue)
            .MaximumParallelMessages(preparation.Value.MaximumConcurrentPreparations);
        options.ListenToRabbitQueue("notification-send")
            .MaximumParallelMessages(delivery.Value.MaximumConcurrentSends);
    }

    private static bool IsDeliveryConflict(Exception exception) =>
        exception is Microsoft.EntityFrameworkCore.DbUpdateException
        {
            InnerException: PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ix_notification_delivery_attempts_notification_id_number"
            }
        };
}
