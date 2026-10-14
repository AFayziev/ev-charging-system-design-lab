using System.Globalization;
using EvCharging.Persistence.Entities;
using HandlebarsDotNet;

namespace EvCharging.NotificationWorker.Templates.Reservation;

public sealed class ReservationEmailRenderer
{
    private readonly HandlebarsTemplate<object, object> _text;
    private readonly HandlebarsTemplate<object, object> _html;

    public ReservationEmailRenderer()
    {
        var text = Handlebars.Create(new HandlebarsConfiguration
        {
            NoEscape = true,
            ThrowOnUnresolvedBindingExpression = true
        });
        var html = Handlebars.Create(new HandlebarsConfiguration
        {
            TextEncoder = new HtmlEncoder(),
            ThrowOnUnresolvedBindingExpression = true
        });
        _text = text.Compile(ReadTemplate("ReservationText.hbs"));
        _html = html.Compile(ReadTemplate("ReservationHtml.hbs"));
    }

    public RenderedEmail Render(ReservationNotificationKind kind, ReservationEmailDetails details)
    {
        string action = kind switch
        {
            ReservationNotificationKind.Created => "created",
            ReservationNotificationKind.Cancelled => "cancelled",
            ReservationNotificationKind.Expired => "expired",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var data = new TemplateData(
            action,
            kind == ReservationNotificationKind.Cancelled,
            kind == ReservationNotificationKind.Expired,
            details.ReservationPublicId.ToString(),
            FormatTime(details.OccurredAt),
            FormatTime(details.StartsAt),
            FormatTime(details.EndsAt),
            details.StationName,
            details.StationAddress,
            details.ChargerName,
            details.ConnectorNumber.ToString(CultureInfo.InvariantCulture));
        return new($"Reservation {action}", _text(data).TrimEnd('\r', '\n'), _html(data));
    }

    private static string FormatTime(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string ReadTemplate(string name)
    {
        using var stream = typeof(ReservationEmailRenderer).Assembly.GetManifestResourceStream(
            $"EvCharging.NotificationWorker.Templates.Reservation.{name}")
            ?? throw new InvalidOperationException($"The embedded email template '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record TemplateData(
        string Action,
        bool Cancelled,
        bool Expired,
        string ReservationId,
        string OccurredAt,
        string StartsAt,
        string EndsAt,
        string StationName,
        string StationAddress,
        string ChargerName,
        string ConnectorNumber);
}
