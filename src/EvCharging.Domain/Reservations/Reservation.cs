using EvCharging.Domain.Auditing;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using FluentResults;

namespace EvCharging.Domain.Reservations;

public sealed class Reservation : IAuditableEntity
{
    private Reservation()
    {
    }

    public Guid Id { get; private set; }
    public Guid PublicId { get; private set; }
    public Guid ConnectorId { get; private set; }
    public Guid DriverId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public ReservationState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Connector? Connector { get; private set; }
    public Driver? Driver { get; private set; }
    public BookingInterval Slot => BookingInterval.Create(StartsAt, EndsAt).Value;

    public static Result<Reservation> Book(
        Connector connector,
        Driver driver,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset now)
    {
        var slot = BookingInterval.Create(start, end);
        if (slot.IsFailed)
        {
            return Result.Fail<Reservation>(slot.Errors);
        }

        if (slot.Value.End <= now)
        {
            return Result.Fail<Reservation>(new BookingValidationError(
                BookingBoundary.End, "The end must follow the current time."));
        }

        return Result.Ok(new Reservation
        {
            Connector = connector,
            ConnectorId = connector.Id,
            Driver = driver,
            DriverId = driver.Id,
            StartsAt = slot.Value.Start,
            EndsAt = slot.Value.End,
            State = ReservationState.Active,
            CreatedAt = now.ToUniversalTime()
        });
    }
}
