using EvCharging.Persistence.Entities;

namespace EvCharging.NotificationWorker.Delivery;

public sealed record NotificationDeliveryResult(
    NotificationDeliveryOutcome Outcome, string? ExternalMessageId = null, string? FailureCode = null);
