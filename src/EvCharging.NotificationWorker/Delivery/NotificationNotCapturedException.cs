namespace EvCharging.NotificationWorker.Delivery;

internal sealed class NotificationNotCapturedException(string? failureCode)
    : Exception($"Notification capture did not occur: {failureCode}.");
