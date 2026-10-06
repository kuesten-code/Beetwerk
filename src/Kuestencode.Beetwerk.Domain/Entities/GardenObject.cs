using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Domain.Entities;

public class GardenObject
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int ObjectTypeId { get; set; }
    public ObjectType? ObjectType { get; set; }
    public required string GeometryGeoJson { get; set; }
    public GeometryKinds GeometryKind { get; set; }
    public string? Notes { get; set; }

    public int? ParentObjectId { get; set; }
    public GardenObject? ParentObject { get; set; }

    public int? PlantSpeciesId { get; set; }
    public PlantSpecies? PlantSpecies { get; set; }

    /// <summary>Werte der typspezifischen Zusatzfelder (Text/Datum), Schlüssel = <see cref="ObjectTypeField.Key"/>.</summary>
    public Dictionary<string, string> Attributes { get; set; } = [];
}
