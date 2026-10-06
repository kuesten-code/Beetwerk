namespace Kuestencode.Beetwerk.Domain.Entities;

public class PlantSpecies
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? ScientificName { get; set; }
    public string? Notes { get; set; }
    public string? ExternalUrl { get; set; }
}
