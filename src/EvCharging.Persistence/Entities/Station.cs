namespace EvCharging.Persistence.Entities;

public sealed class Station
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public required string TimeZoneId { get; set; }
    public required string AccessInstructions { get; set; }
}
