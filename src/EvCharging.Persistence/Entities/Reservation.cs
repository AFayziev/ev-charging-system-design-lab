using EvCharging.Domain.Reservations;

namespace EvCharging.Persistence.Entities;

public sealed class Reservation
{
    public long Id { get; set; }
    public long ConnectorId { get; set; }
    public long DriverId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public ReservationState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
