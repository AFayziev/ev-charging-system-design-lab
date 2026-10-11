namespace EvCharging.Domain.Equipment;

/// <summary>An external identity scoped by integration and, when applicable, its parent.</summary>
public sealed record ProviderIdentity
{
    public ProviderIdentity(Guid providerId, string externalId, string? parentExternalId = null)
    {
        if (providerId == Guid.Empty)
        {
            throw new ArgumentException("A provider ID is required.", nameof(providerId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        if (parentExternalId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parentExternalId);
        }

        ProviderId = providerId;
        ExternalId = externalId;
        ParentExternalId = parentExternalId;
    }

    public Guid ProviderId { get; }
    public string ExternalId { get; }
    public string? ParentExternalId { get; }
}
