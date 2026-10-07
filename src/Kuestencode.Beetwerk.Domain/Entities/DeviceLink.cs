namespace Kuestencode.Beetwerk.Domain.Entities;

/// <summary>Verbindet ein Objekt auf der Karte (meist vom Typ „Gerät“) mit einem Gerät bei einem Provider.</summary>
public class DeviceLink
{
    public int Id { get; set; }
    public int ObjectId { get; set; }
    public GardenObject? Object { get; set; }
    public required string Provider { get; set; }
    public required string ExternalId { get; set; }
    /// <summary>Providerspezifische Einstellungen, z. B. Entity-IDs für Akku und Schnitthöhe bei Home Assistant.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];
    public bool CreateTasksOnError { get; set; } = true;
    /// <summary>Offene Aufgabe zum aktuellen Fehler; verhindert, dass bei jeder Abfrage eine neue entsteht.</summary>
    public int? ErrorTaskId { get; set; }
}
