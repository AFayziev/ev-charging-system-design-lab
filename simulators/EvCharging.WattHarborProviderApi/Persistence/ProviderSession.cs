namespace EvCharging.WattHarborProviderApi.Persistence;

internal sealed class ProviderSession
{
    private const decimal WattHoursPerKilowattHour = 1000m;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid PublicId { get; private set; } = Guid.NewGuid();
    public Guid ExternalSessionId { get; init; }
    public string StationId { get; init; } = "";
    public string ChargerId { get; init; } = "";
    public string ConnectorId { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; private set; }
    public decimal PowerKw { get; init; }

    /// <summary>Cumulative energy follows from the stored start, so it continues unchanged across restarts.</summary>
    public decimal EnergyWhAt(DateTimeOffset at)
    {
        var elapsedSeconds = (decimal)(at - StartedAt).TotalSeconds;
        return Math.Round(PowerKw * WattHoursPerKilowattHour * Math.Max(0m, elapsedSeconds) / 3600m, 3);
    }

    public bool HasTaken(decimal energyKWh, DateTimeOffset at)
        => EnergyWhAt(at) >= energyKWh * WattHoursPerKilowattHour;
}
