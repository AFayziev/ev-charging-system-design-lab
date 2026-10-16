namespace EvCharging.SynchronousChargingProviderApi.Persistence;

internal sealed class ProviderCommand
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid PublicId { get; private set; } = Guid.NewGuid();
    public Guid Reference { get; init; }
    public string StationId { get; init; } = "";
    public string ChargerId { get; init; } = "";
    public string ConnectorId { get; init; } = "";
    public Guid SessionReference { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool? Applied { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? RejectionCode { get; private set; }
    public Guid? SessionId { get; private set; }
    public ProviderSession? Session { get; private set; }

    public void Apply(ProviderSession session, DateTimeOffset now)
    {
        Applied = true;
        CompletedAt = now;
        SessionId = session.Id;
        Session = session;
    }

    public void Reject(string code, DateTimeOffset now)
    {
        Applied = false;
        CompletedAt = now;
        RejectionCode = code;
    }
}
