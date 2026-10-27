namespace EvCharging.Persistence.Entities;

/// <summary>Dispatch state and capture evidence for one notification delivery attempt.</summary>
public enum NotificationDeliveryOutcome
{
    /// <summary>
    /// Dispatch was authorized, but capture is unproven. This includes an in-flight call, an interrupted
    /// send, or an inconclusive response. The attempt cannot be dispatched again or authorize a retry.
    /// </summary>
    Uncertain = 1,

    /// <summary>
    /// The sink or provider accepted the message and returned a message ID. This does not establish
    /// delivery to a real recipient.
    /// </summary>
    Captured = 2,

    /// <summary>
    /// A permanent failure with evidence that capture did not occur. No automatic retry is permitted.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// A temporary failure with evidence that capture did not occur. Wolverine may retry the delivery job
    /// under its error policy; this outcome is retained if those retries are exhausted.
    /// </summary>
    NotCaptured = 4
}
