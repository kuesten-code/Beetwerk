using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Domain.Entities;

public class ObjectType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Icon { get; set; } = "📍";
    public string Color { get; set; } = "#4caf50";
    public GeometryKinds AllowedGeometries { get; set; } = GeometryKinds.All;
    public List<ObjectTypeField> Fields { get; set; } = [];

    public bool Allows(GeometryKinds kind) => (AllowedGeometries & kind) == kind;
}
