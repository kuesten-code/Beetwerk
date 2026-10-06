namespace Kuestencode.Beetwerk.Domain.Entities;

public class ObjectPhoto
{
    public int Id { get; set; }
    public int ObjectId { get; set; }
    public GardenObject? Object { get; set; }
    public required string FileName { get; set; }
    public required string ThumbnailFileName { get; set; }
    public required string ContentType { get; set; }
    public string? Caption { get; set; }
    public DateOnly TakenOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
