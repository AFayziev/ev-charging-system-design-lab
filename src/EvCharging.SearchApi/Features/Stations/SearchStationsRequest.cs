using System.ComponentModel.DataAnnotations;

namespace EvCharging.SearchApi.Features.Stations;

/// <summary>Every parameter is optional; they combine freely.</summary>
public sealed record SearchStationsRequest(
    [property: MaxLength(StationSearchLimits.MaximumTextLength)] string? Q = null,
    [property: MaxLength(StationSearchLimits.MaximumFilterValues)] string[]? ConnectorType = null,
    [property: MaxLength(StationSearchLimits.MaximumFilterValues)] string[]? PowerSupply = null,
    [property: Range(0d, StationSearchLimits.MaximumPowerKw)] decimal? MinPowerKw = null,
    [property: Range(-90d, 90d)] double? Lat = null,
    [property: Range(-180d, 180d)] double? Lon = null,
    [property: Range(StationSearchLimits.MinimumRadiusKm, StationSearchLimits.MaximumRadiusKm)] double? RadiusKm = null,
    [property: Range(1, StationSearchLimits.MaximumPageSize)] int Limit = StationSearchLimits.DefaultPageSize,
    [property: MaxLength(StationSearchLimits.MaximumCursorLength)] string? Cursor = null) : IValidatableObject
{
    internal StationSearchSort Sort => (Lat, Q) switch
    {
        (not null, _) => StationSearchSort.Distance,
        (_, { } text) when !string.IsNullOrWhiteSpace(text) => StationSearchSort.Relevance,
        _ => StationSearchSort.Name
    };

    private bool HasSearchParameters =>
        Q is not null || ConnectorType is { Length: > 0 } || PowerSupply is { Length: > 0 } || MinPowerKw is not null
        || Lat is not null || Lon is not null || RadiusKm is not null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Q?.Trim() is { Length: > 0 and < StationSearchLimits.MinimumTextLength })
        {
            yield return new ValidationResult(
                $"Text must be at least {StationSearchLimits.MinimumTextLength} characters.", [nameof(Q)]);
        }

        if ((Lat is null) != (Lon is null))
        {
            yield return new ValidationResult(
                "Latitude and longitude go together.", [Lat is null ? nameof(Lat) : nameof(Lon)]);
        }

        if (RadiusKm is not null && Lat is null)
        {
            yield return new ValidationResult("The radius needs a location.", [nameof(RadiusKm)]);
        }

        if (ConnectorType?.Any(IsInvalidFilterValue) == true)
        {
            yield return InvalidFilter(nameof(ConnectorType));
        }

        if (PowerSupply?.Any(IsInvalidFilterValue) == true)
        {
            yield return InvalidFilter(nameof(PowerSupply));
        }

        if (Cursor is not null && HasSearchParameters)
        {
            yield return new ValidationResult(
                "The cursor continues its own search and cannot be combined with search parameters.",
                [nameof(Cursor)]);
        }
    }

    private static ValidationResult InvalidFilter(string property) => new(
        $"Values must be non-empty and at most {StationSearchLimits.MaximumFilterValueLength} characters.",
        [property]);

    private static bool IsInvalidFilterValue(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > StationSearchLimits.MaximumFilterValueLength;
}
