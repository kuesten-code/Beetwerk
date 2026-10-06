namespace Kuestencode.Beetwerk.Domain.Entities;

public class PushSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public required string Endpoint { get; set; }
    public required string P256dh { get; set; }
    public required string Auth { get; set; }
    public string? DeviceLabel { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
