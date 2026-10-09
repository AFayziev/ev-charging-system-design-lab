namespace EvCharging.Domain.Equipment;

/// <summary>An external identity scoped by integration and, when applicable, its parent.</summary>
public sealed record ProviderIdentity
{
    public ProviderIdentity(long providerId, string externalId, string? parentExternalId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        if (parentExternalId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parentExternalId);
        }

        ProviderId = providerId;
        ExternalId = externalId;
        ParentExternalId = parentExternalId;
    }

    public long ProviderId { get; }
    public string ExternalId { get; }
    public string? ParentExternalId { get; }
}
