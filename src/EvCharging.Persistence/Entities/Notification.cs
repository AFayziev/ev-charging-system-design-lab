namespace EvCharging.Persistence.Entities;

public sealed class Notification
{
    public Guid Id { get; init; }
    public Guid PublicId { get; init; }
    public required string RecipientEmail { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public string? HtmlBody { get; init; }
    public DateTimeOffset PreparedAt { get; init; }
}
