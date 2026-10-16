namespace EvCharging.Persistence.Migrations.SeedData;

internal sealed class LabDataOptions
{
    public const string SectionName = "LabData";

    public bool Enabled { get; set; }
    public bool Reset { get; set; }
    public string? IdentityIssuer { get; set; }
    public string? WattHarborIntegrationKey { get; set; }
    public string? AmpTrailIntegrationKey { get; set; }
}
