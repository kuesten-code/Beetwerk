namespace Kuestencode.Beetwerk.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    /// <summary>Leer beim impliziten Standardnutzer im Modus AUTH_MODE=none.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Ändert sich bei jedem Passwortwechsel; Sitzungen mit altem Stempel werden abgewiesen.</summary>
    public string SecurityStamp { get; set; } = "";
    public List<PushSubscription> PushSubscriptions { get; set; } = [];
}
