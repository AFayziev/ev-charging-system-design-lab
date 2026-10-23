namespace EvCharging.ChargingWorker.Handlers;

public sealed record ExpireReservationMessage(Guid ReservationId);
