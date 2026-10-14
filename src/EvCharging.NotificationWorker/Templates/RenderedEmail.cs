namespace EvCharging.NotificationWorker.Templates;

public sealed record RenderedEmail(string Subject, string TextBody, string HtmlBody);
