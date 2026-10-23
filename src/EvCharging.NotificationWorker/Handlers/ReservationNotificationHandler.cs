using System.Diagnostics;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Contracts.Events.Sessions;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.NotificationWorker.Templates.Reservation;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace EvCharging.NotificationWorker.Handlers;

public static class ReservationNotificationHandler
{
    [Transactional]
    public static Task<SendNotification?> HandleAsync(
        ReservationCreated message,
        PlatformDbContext database,
        ReservationEmailRenderer renderer,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        PrepareAsync(
            message.ReservationId,
            ReservationNotificationKind.Created,
            null,
            database,
            renderer,
            clock,
            cancellationToken);

    [Transactional]
    public static Task<SendNotification?> HandleAsync(
        ReservationCancelled message,
        PlatformDbContext database,
        ReservationEmailRenderer renderer,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        PrepareAsync(
            message.ReservationId,
            ReservationNotificationKind.Cancelled,
            null,
            database,
            renderer,
            clock,
            cancellationToken);

    [Transactional]
    public static Task<SendNotification?> HandleAsync(
        ReservationExpired message,
        PlatformDbContext database,
        ReservationEmailRenderer renderer,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        PrepareAsync(
            message.ReservationId,
            ReservationNotificationKind.Expired,
            null,
            database,
            renderer,
            clock,
            cancellationToken);

    [Transactional]
    public static Task<SendNotification?> HandleAsync(
        SessionCompleted message,
        PlatformDbContext database,
        ReservationEmailRenderer renderer,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        PrepareAsync(
            message.ReservationId,
            ReservationNotificationKind.ChargingCompleted,
            message,
            database,
            renderer,
            clock,
            cancellationToken);

    private static async Task<SendNotification?> PrepareAsync(
        Guid reservationId,
        ReservationNotificationKind kind,
        SessionCompleted? completion,
        PlatformDbContext database,
        ReservationEmailRenderer renderer,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await database.ReservationNotifications.AnyAsync(
            notification => notification.ReservationId == reservationId && notification.Kind == kind,
            cancellationToken))
        {
            return null;
        }

        var reservation = await database.Reservations
            .AsNoTracking()
            .Include(reservation => reservation.Driver)
            .Include(reservation => reservation.Connector!)
            .ThenInclude(connector => connector.Charger!)
            .ThenInclude(charger => charger.Station)
            .SingleOrDefaultAsync(reservation => reservation.Id == reservationId, cancellationToken)
            ?? throw new InvalidOperationException("Notification preparation requires an existing reservation.");
        var notification = CreateNotification(reservation, kind, completion, renderer, clock.GetUtcNow());
        database.Notifications.Add(notification);
        database.ReservationNotifications.Add(new ReservationNotification
        {
            ReservationId = reservation.Id,
            Kind = kind,
            NotificationId = notification.Id
        });
        Activity.Current?.SetTag("reservation.public_id", reservation.PublicId.ToString());

        return new SendNotification(notification.Id);
    }

    private static Notification CreateNotification(
        Reservation reservation,
        ReservationNotificationKind kind,
        SessionCompleted? completion,
        ReservationEmailRenderer renderer,
        DateTimeOffset preparedAt)
    {
        var driver = reservation.Driver
            ?? throw new InvalidOperationException("Notification preparation requires a driver profile.");
        if (EmailAddress.Create(driver.Email).IsFailed)
        {
            throw new InvalidOperationException("Notification preparation requires a valid stored recipient email.");
        }

        var connector = reservation.Connector
            ?? throw new InvalidOperationException("Notification preparation requires a connector.");
        var charger = connector.Charger
            ?? throw new InvalidOperationException("Notification preparation requires a charger.");
        var station = charger.Station
            ?? throw new InvalidOperationException("Notification preparation requires a station.");
        DateTimeOffset occurredAt = kind switch
        {
            ReservationNotificationKind.Created => reservation.CreatedAt,
            ReservationNotificationKind.Cancelled => reservation.CancelledAt
                ?? throw new InvalidOperationException("The reservation has no cancellation fact."),
            ReservationNotificationKind.Expired => reservation.ExpiredAt
                ?? throw new InvalidOperationException("The reservation has no expiry fact."),
            ReservationNotificationKind.ChargingCompleted => completion?.CompletedAt
                ?? throw new InvalidOperationException("A completion notification requires the completion event."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var details = new ReservationEmailDetails(
            reservation.PublicId,
            occurredAt,
            reservation.StartsAt,
            reservation.EndsAt,
            station.Name,
            station.Address,
            charger.Name,
            connector.Number,
            completion?.FinalEnergyKWh);
        var content = renderer.Render(kind, details);
        return new Notification
        {
            RecipientEmail = driver.Email,
            Subject = content.Subject,
            Body = content.TextBody,
            HtmlBody = content.HtmlBody,
            PreparedAt = preparedAt
        };
    }
}
