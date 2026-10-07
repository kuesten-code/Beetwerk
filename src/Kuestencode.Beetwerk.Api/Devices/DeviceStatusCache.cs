using System.Collections.Concurrent;
using Kuestencode.Beetwerk.Domain.Devices;

namespace Kuestencode.Beetwerk.Api.Devices;

public sealed record CachedDeviceStatus(DeviceStatus? Status, string? Error, DateTimeOffset FetchedAt);

/// <summary>Zuletzt abgefragter Status je Geräteverknüpfung. Lebt im Speicher – nach einem Neustart wird neu abgefragt.</summary>
public sealed class DeviceStatusCache
{
    private readonly ConcurrentDictionary<int, CachedDeviceStatus> entries = new();

    public CachedDeviceStatus? Get(int linkId) => entries.GetValueOrDefault(linkId);

    public void Set(int linkId, CachedDeviceStatus entry) => entries[linkId] = entry;

    /// <summary>Nach einem Befehl veraltet der Status – die nächste Runde der Überwachung fragt dann sofort nach.</summary>
    public void Invalidate(int linkId)
    {
        if (entries.TryGetValue(linkId, out var entry))
            entries[linkId] = entry with { FetchedAt = DateTimeOffset.MinValue };
    }

    public void Remove(int linkId) => entries.TryRemove(linkId, out _);
}
