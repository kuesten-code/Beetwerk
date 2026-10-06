namespace Kuestencode.Beetwerk.Domain.Entities;

/// <summary>Eigenes Bild über dem Luftbild, z. B. Drohnenaufnahme oder gezeichneter Gartenplan.</summary>
public class MapOverlay
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Vier Ecken [lng, lat] in der Reihenfolge oben links, oben rechts, unten rechts, unten links.</summary>
    public required string CornersJson { get; set; }
    public double Opacity { get; set; } = 1.0;
    public bool Visible { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}
