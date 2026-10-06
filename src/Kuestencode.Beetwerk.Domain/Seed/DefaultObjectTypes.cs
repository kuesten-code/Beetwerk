using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Domain.Seed;

public static class DefaultObjectTypes
{
    public static IReadOnlyList<ObjectType> Create() =>
    [
        new()
        {
            Name = "Pflanze", Icon = "🌱", Color = "#43a047",
            AllowedGeometries = GeometryKinds.Point | GeometryKinds.Polygon,
            Fields =
            [
                new("species", "Pflanzenart", ObjectFieldType.Species),
                new("variety", "Sorte", ObjectFieldType.Text),
                new("plantedOn", "Pflanzdatum", ObjectFieldType.Date)
            ]
        },
        new() { Name = "Beet", Icon = "🟫", Color = "#8d6e63", AllowedGeometries = GeometryKinds.Polygon },
        new()
        {
            Name = "Hecke", Icon = "🌳", Color = "#2e7d32",
            AllowedGeometries = GeometryKinds.LineString | GeometryKinds.Polygon,
            Fields =
            [
                new("species", "Art", ObjectFieldType.Species),
                new("plantedOn", "Pflanzdatum", ObjectFieldType.Date)
            ]
        },
        new() { Name = "Gewächshaus", Icon = "🏠", Color = "#90caf9", AllowedGeometries = GeometryKinds.Polygon },
        new() { Name = "Stall", Icon = "🐔", Color = "#ffb74d", AllowedGeometries = GeometryKinds.Polygon },
        new()
        {
            Name = "Gerät", Icon = "⚙️", Color = "#78909c",
            AllowedGeometries = GeometryKinds.Point | GeometryKinds.Polygon
        },
        new() { Name = "Sonstiges", Icon = "📍", Color = "#ab47bc", AllowedGeometries = GeometryKinds.All }
    ];
}
