namespace Kuestencode.Beetwerk.Domain.Entities;

/// <summary>Singleton: eine Installation verwaltet genau einen Garten.</summary>
public class Garden
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string Name { get; set; } = "Mein Garten";
    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
    public double Zoom { get; set; }
    public string? BoundaryGeoJson { get; set; }
    /// <summary>Abstand in Metern, unter dem schlechte Nachbarn auf der Karte gewarnt werden.</summary>
    public double NeighborWarningDistance { get; set; } = 1.0;
}
