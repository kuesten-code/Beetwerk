namespace Kuestencode.Beetwerk.Domain.Enums;

[Flags]
public enum GeometryKinds
{
    None = 0,
    Point = 1,
    LineString = 2,
    Polygon = 4,
    All = Point | LineString | Polygon
}
