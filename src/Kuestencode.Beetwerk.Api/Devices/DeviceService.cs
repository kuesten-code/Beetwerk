using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Push;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Devices;
using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Devices;

public class DeviceService(
    IEnumerable<IDeviceProvider> providers,
    DeviceStatusCache cache,
    BeetwerkDbContext db,
    PushDispatcher push,
    BeetwerkOptions options,
    TimeProvider time,
    ILogger<DeviceService> logger)
{
    /// <summary>Auch eine manuelle Aktualisierung fragt höchstens so oft nach – schützt die Monatsgrenze der Husqvarna-API.</summary>
    public static readonly TimeSpan MinimumRefreshGap = TimeSpan.FromSeconds(20);

    private readonly Dictionary<string, IDeviceProvider> byKey = providers.ToDictionary(p => p.Key);

    public IEnumerable<IDeviceProvider> Providers => byKey.Values;

    public IDeviceProvider? Provider(string key) => byKey.GetValueOrDefault(key);

    public static DeviceRef RefOf(DeviceLink link) => new(link.ExternalId, link.Settings);

    /// <summary>Fragt den Status neu ab, wenn der zwischengespeicherte zu alt ist (oder <paramref name="force"/>).</summary>
    public async Task<CachedDeviceStatus> RefreshAsync(DeviceLink link, bool force, CancellationToken ct)
    {
        var provider = Provider(link.Provider);
        var now = time.GetUtcNow();
        var cached = cache.Get(link.Id);
        if (cached is not null)
        {
            var age = now - cached.FetchedAt;
            if (age < MinimumRefreshGap || (!force && provider is not null && age < provider.PollInterval))
                return cached;
        }

        CachedDeviceStatus entry;
        if (provider is null || !provider.IsConfigured)
        {
            entry = new CachedDeviceStatus(null, $"Anbieter „{link.Provider}“ ist auf dem Server nicht eingerichtet.", now);
        }
        else
        {
            try
            {
                var status = await provider.GetStatusAsync(RefOf(link), ct);
                entry = new CachedDeviceStatus(status, status is null ? "Das Gerät wurde beim Anbieter nicht gefunden." : null, now);
                if (status is not null)
                    await HandleErrorStateAsync(link, status, ct);
            }
            catch (DeviceProviderException ex)
            {
                logger.LogWarning("Status von {Provider}/{Device} nicht abrufbar: {Message}", link.Provider, link.ExternalId, ex.Message);
                entry = new CachedDeviceStatus(cached?.Status, ex.Message, now);
            }
        }

        cache.Set(link.Id, entry);
        return entry;
    }

    /// <summary>
    /// Meldet das Gerät einen Fehler, entsteht genau eine Aufgabe am Objekt (mit sofortigem Push).
    /// Eine weitere gibt es erst, wenn die vorige erledigt oder gelöscht ist.
    /// </summary>
    public async Task<GardenTask?> HandleErrorStateAsync(DeviceLink link, DeviceStatus status, CancellationToken ct)
    {
        if (!status.HasError || !link.CreateTasksOnError)
            return null;

        if (link.ErrorTaskId is { } existingId
            && await db.Tasks.AnyAsync(t => t.Id == existingId && t.Status == GardenTaskStatus.Open, ct))
            return null;

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var task = new GardenTask
        {
            Title = $"{status.Name}: {status.ErrorText ?? "Fehler"}",
            Description = "Automatisch angelegt, weil das Gerät einen Fehler meldet. Nach dem Beheben die Aufgabe als erledigt markieren.",
            ObjectId = link.ObjectId,
            DueDate = today,
            SeriesStart = today,
            Notify = true,
            NotifiedOn = options.PushEnabled ? today : null
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        // Die Verknüpfung frisch laden: Sie kann aus einem anderen Kontext stammen (Überwachung im Hintergrund).
        var tracked = await db.DeviceLinks.FirstAsync(l => l.Id == link.Id, ct);
        tracked.ErrorTaskId = task.Id;
        link.ErrorTaskId = task.Id;
        await db.SaveChangesAsync(ct);

        if (options.PushEnabled)
            await push.SendAsync(new PushMessage($"⚠ {status.Name}", status.ErrorText ?? "Das Gerät meldet einen Fehler.", $"/aufgaben/{task.Id}", $"device-{link.Id}"), null, ct);

        logger.LogInformation("Gerätefehler bei {Device}: Aufgabe {TaskId} angelegt.", status.Name, task.Id);
        return task;
    }

    public async Task SendCommandAsync(DeviceLink link, DeviceCommand command, int? durationMinutes, CancellationToken ct)
    {
        var provider = ConfiguredProvider(link);
        if (!provider.SupportedCommands.Contains(command))
            throw new DeviceProviderException("Dieser Befehl wird von diesem Anbieter nicht unterstützt.");
        await provider.SendCommandAsync(RefOf(link), command, durationMinutes, ct);
        cache.Invalidate(link.Id);
    }

    public async Task SetCuttingHeightAsync(DeviceLink link, int height, CancellationToken ct)
    {
        var provider = ConfiguredProvider(link);
        await provider.SetCuttingHeightAsync(RefOf(link), height, ct);
        cache.Invalidate(link.Id);
    }

    private IDeviceProvider ConfiguredProvider(DeviceLink link)
    {
        var provider = Provider(link.Provider);
        if (provider is null || !provider.IsConfigured)
            throw new DeviceProviderException($"Anbieter „{link.Provider}“ ist auf dem Server nicht eingerichtet.");
        return provider;
    }
}
