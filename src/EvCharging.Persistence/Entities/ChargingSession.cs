using EvCharging.Domain.Charging;

namespace EvCharging.Persistence.Entities;

public sealed class ChargingSession
{
    public long Id { get; set; }
    public long ReservationId { get; set; }
    public long ConnectorId { get; set; }
    public SessionLifecycle Lifecycle { get; set; }
    public long? ChargingProviderId { get; set; }
    public string? ProviderSessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public decimal? FinalEnergyKWh { get; set; }
}
