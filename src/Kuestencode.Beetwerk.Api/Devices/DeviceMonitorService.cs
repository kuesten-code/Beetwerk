using Kuestencode.Beetwerk.Data;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Devices;

/// <summary>
/// Fragt verknüpfte Geräte im Hintergrund ab. Läuft kurz getaktet, fragt aber je Gerät nur so oft nach,
/// wie es der Provider erlaubt (Husqvarna standardmäßig alle 10 Minuten, Home Assistant jede Minute).
/// </summary>
public class DeviceMonitorService(IServiceScopeFactory scopes, ILogger<DeviceMonitorService> logger, TimeProvider time) : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BeetwerkDbContext>();
                var devices = scope.ServiceProvider.GetRequiredService<DeviceService>();
                foreach (var link in await db.DeviceLinks.ToListAsync(stoppingToken))
                    await devices.RefreshAsync(link, force: false, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Abfrage der Geräte fehlgeschlagen.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
