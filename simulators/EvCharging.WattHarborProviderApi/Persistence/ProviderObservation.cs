namespace EvCharging.WattHarborProviderApi.Persistence;

public enum ObservationState
{
    Charging,
    Stopped
}

public enum ObservationDeliveryStatus
{
    Pending,
    Delivered,
    Abandoned,
    Suppressed
}

internal sealed class ProviderObservation
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid EventId { get; private set; } = Guid.NewGuid();
    public Guid SessionId { get; init; }
    public ProviderSession? Session { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public ObservationState State { get; init; }
    public decimal PowerKw { get; init; }
    public decimal EnergyWh { get; init; }
    public bool IsFinalEnergy { get; init; }
    public ObservationDeliveryStatus DeliveryStatus { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public int? LastStatusCode { get; private set; }

    public static ProviderObservation Charging(ProviderSession session, DateTimeOffset occurredAt) =>
        Create(session, ObservationState.Charging, occurredAt);

    public static ProviderObservation Stopped(ProviderSession session, DateTimeOffset endedAt) =>
        Create(session, ObservationState.Stopped, endedAt);

    public void MarkDelivered(DateTimeOffset now, int statusCode)
    {
        DeliveryStatus = ObservationDeliveryStatus.Delivered;
        DeliveredAt = now;
        LastStatusCode = statusCode;
    }

    public void MarkFailed(DateTimeOffset now, int? statusCode, int maxAttempts, TimeSpan retryDelay)
    {
        Attempts++;
        LastStatusCode = statusCode;
        if (Attempts >= maxAttempts)
        {
            DeliveryStatus = ObservationDeliveryStatus.Abandoned;
            return;
        }

        NextAttemptAt = now + (retryDelay * Attempts);
    }

    private static ProviderObservation Create(ProviderSession session, ObservationState state, DateTimeOffset at) =>
        new()
        {
            SessionId = session.Id,
            OccurredAt = at,
            State = state,
            PowerKw = session.PowerKw,
            EnergyWh = session.EnergyWhAt(at),
            IsFinalEnergy = state == ObservationState.Stopped,
            NextAttemptAt = at
        };

    public void Suppress() => DeliveryStatus = ObservationDeliveryStatus.Suppressed;
}

internal sealed class CallbackTarget
{
    public const int SingletonId = 1;

    public int Id { get; init; } = SingletonId;
    public string Url { get; init; } = "";
    public string ApiKey { get; init; } = "";
}
