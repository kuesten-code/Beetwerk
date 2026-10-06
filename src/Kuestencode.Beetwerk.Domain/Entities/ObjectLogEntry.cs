using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Domain.Entities;

/// <summary>Eintrag im Verlauf eines Objekts: eigene Notizen sowie automatisch erfasste Ereignisse.</summary>
public class ObjectLogEntry
{
    public int Id { get; set; }
    public int ObjectId { get; set; }
    public GardenObject? Object { get; set; }
    public DateOnly Date { get; set; }
    public ObjectLogKind Kind { get; set; }
    public required string Text { get; set; }
    public int? TaskId { get; set; }
    public int? PhotoId { get; set; }
    public ObjectPhoto? Photo { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
