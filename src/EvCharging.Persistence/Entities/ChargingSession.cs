using EvCharging.Domain.Charging;

namespace EvCharging.Persistence.Entities;

public sealed class ChargingSession
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public Guid ConnectorId { get; set; }
    public SessionLifecycle Lifecycle { get; set; }
    public Guid? ChargingProviderId { get; set; }
    public string? ProviderSessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public decimal? FinalEnergyKWh { get; set; }
}
