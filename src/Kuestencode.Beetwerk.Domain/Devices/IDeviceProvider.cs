namespace Kuestencode.Beetwerk.Domain.Devices;

/// <summary>
/// Anbindung an eine Geräteplattform (z. B. Husqvarna Automower Connect, Home Assistant).
/// Ein Provider kennt nur seine eigenen Geräte-IDs; die Zuordnung zu Objekten auf der Karte liegt in <c>DeviceLink</c>.
/// </summary>
public interface IDeviceProvider
{
    /// <summary>Stabiler Schlüssel, der in der Datenbank gespeichert wird, z. B. "husqvarna".</summary>
    string Key { get; }
    string DisplayName { get; }
    /// <summary>False, wenn die Zugangsdaten in der Konfiguration fehlen.</summary>
    bool IsConfigured { get; }
    /// <summary>Kürzester sinnvoller Abstand zwischen zwei Abfragen (Rate-Limits der Plattform).</summary>
    TimeSpan PollInterval { get; }
    IReadOnlySet<DeviceCommand> SupportedCommands { get; }

    Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct);
    Task<DeviceStatus?> GetStatusAsync(DeviceRef device, CancellationToken ct);
    Task SendCommandAsync(DeviceRef device, DeviceCommand command, int? durationMinutes, CancellationToken ct);
    Task SetCuttingHeightAsync(DeviceRef device, int height, CancellationToken ct);
}

/// <summary>Verweis auf ein Gerät beim Provider samt optionaler, providerspezifischer Einstellungen.</summary>
public sealed record DeviceRef(string ExternalId, IReadOnlyDictionary<string, string> Settings)
{
    public string? Setting(string key) => Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

public sealed record DeviceInfo(string ExternalId, string Name, string? Model);

public enum DeviceCommand
{
    /// <summary>Mähen für eine Dauer, unabhängig vom Zeitplan.</summary>
    Start,
    Pause,
    /// <summary>Zurück zur Ladestation und dort bleiben, bis wieder gestartet wird.</summary>
    ParkUntilFurtherNotice,
    /// <summary>Zurück zur Ladestation, beim nächsten Termin im Zeitplan geht es weiter.</summary>
    ParkUntilNextSchedule,
    /// <summary>Wieder dem Zeitplan folgen.</summary>
    ResumeSchedule
}

public enum DeviceActivity
{
    Unknown,
    Mowing,
    GoingHome,
    Charging,
    Leaving,
    Parked,
    Paused,
    Stopped,
    Error,
    Offline
}

public sealed record DeviceStatus
{
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public DeviceActivity Activity { get; init; }
    /// <summary>Rohzustand der Plattform für Anzeige und Fehlersuche, z. B. "IN_OPERATION".</summary>
    public string? RawState { get; init; }
    public int? BatteryPercent { get; init; }
    public int? CuttingHeight { get; init; }
    public int CuttingHeightMin { get; init; } = 1;
    public int CuttingHeightMax { get; init; } = 9;
    public int? ErrorCode { get; init; }
    public string? ErrorText { get; init; }
    public DateTimeOffset? NextStart { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public bool Connected { get; init; } = true;
    public DateTimeOffset UpdatedAt { get; init; }

    public bool HasError => Activity == DeviceActivity.Error;
}

/// <summary>Fehler der Geräteplattform (nicht erreichbar, Zugangsdaten falsch, Befehl abgelehnt).</summary>
public class DeviceProviderException(string message, Exception? inner = null) : Exception(message, inner);
