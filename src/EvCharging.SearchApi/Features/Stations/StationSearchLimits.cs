namespace EvCharging.SearchApi.Features.Stations;

internal static class StationSearchLimits
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 50;
    public const int MaximumTextLength = 100;
    public const int MaximumFilterValues = 10;
    public const int MaximumFilterValueLength = 50;
    public const double MaximumPowerKw = 1000;
    public const double MinimumRadiusKm = 0.001;
    public const double MaximumRadiusKm = 500;
    public const int MaximumCursorLength = 4096;
}
