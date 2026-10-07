using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Api.Devices;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Devices;
using Kuestencode.Beetwerk.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/devices/providers", (DeviceService devices) =>
            devices.Providers.Select(p => new DeviceProviderDto(
                p.Key, p.DisplayName, p.IsConfigured, p.SupportedCommands.Order().ToList(), p.Key == HusqvarnaProvider.ProviderKey)));

        api.MapGet("/devices/providers/{key}/devices", async (string key, DeviceService devices, CancellationToken ct) =>
        {
            var provider = devices.Provider(key);
            if (provider is null)
                return Results.NotFound();
            if (!provider.IsConfigured)
                return ApiResults.Conflict($"{provider.DisplayName} ist auf dem Server nicht eingerichtet.");
            try
            {
                return Results.Ok(await provider.ListDevicesAsync(ct));
            }
            catch (DeviceProviderException ex)
            {
                return ApiResults.Error(ex.Message, StatusCodes.Status502BadGateway);
            }
        });

        api.MapGet("/devices", async (BeetwerkDbContext db, DeviceStatusCache cache, CancellationToken ct) =>
            (await db.DeviceLinks.ToListAsync(ct)).Select(link => ToDto(link, cache.Get(link.Id))));

        var device = api.MapGroup("/objects/{objectId:int}/device");

        device.MapPut("/", async (int objectId, DeviceLinkInput input, BeetwerkDbContext db, DeviceService devices,
            DeviceStatusCache cache, CancellationToken ct) =>
        {
            if (!await db.Objects.AnyAsync(o => o.Id == objectId, ct))
                return Results.NotFound();
            if (devices.Provider(input.Provider) is null)
                return ApiResults.Error("Unbekannter Anbieter.");
            if (string.IsNullOrWhiteSpace(input.ExternalId))
                return ApiResults.Error("Bitte ein Gerät auswählen.");

            var link = await db.DeviceLinks.FirstOrDefaultAsync(l => l.ObjectId == objectId, ct);
            if (link is null)
            {
                link = new DeviceLink { ObjectId = objectId, Provider = "", ExternalId = "" };
                db.DeviceLinks.Add(link);
            }
            else if (link.Provider != input.Provider || link.ExternalId != input.ExternalId.Trim())
            {
                link.ErrorTaskId = null;
            }
            link.Provider = input.Provider;
            link.ExternalId = input.ExternalId.Trim();
            link.Settings = (input.Settings ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s.Value))
                .ToDictionary(s => s.Key, s => s.Value.Trim());
            link.CreateTasksOnError = input.CreateTasksOnError;
            await db.SaveChangesAsync(ct);

            cache.Remove(link.Id);
            var status = await devices.RefreshAsync(link, force: true, ct);
            return Results.Ok(ToDto(link, status));
        });

        device.MapDelete("/", async (int objectId, BeetwerkDbContext db, DeviceStatusCache cache, CancellationToken ct) =>
        {
            var link = await db.DeviceLinks.FirstOrDefaultAsync(l => l.ObjectId == objectId, ct);
            if (link is null)
                return Results.NotFound();
            db.DeviceLinks.Remove(link);
            await db.SaveChangesAsync(ct);
            cache.Remove(link.Id);
            return Results.NoContent();
        });

        device.MapPost("/refresh", async (int objectId, BeetwerkDbContext db, DeviceService devices, CancellationToken ct) =>
            await db.DeviceLinks.FirstOrDefaultAsync(l => l.ObjectId == objectId, ct) is { } link
                ? Results.Ok(ToDto(link, await devices.RefreshAsync(link, force: true, ct)))
                : Results.NotFound());

        device.MapPost("/commands", async (int objectId, DeviceCommandInput input, BeetwerkDbContext db, DeviceService devices, CancellationToken ct) =>
        {
            var link = await db.DeviceLinks.FirstOrDefaultAsync(l => l.ObjectId == objectId, ct);
            if (link is null)
                return Results.NotFound();
            if (!Enum.IsDefined(input.Command))
                return ApiResults.Error("Unbekannter Befehl.");
            if (input.Command == DeviceCommand.Start && input.DurationMinutes is < 10 or > 1440)
                return ApiResults.Error("Die Mähdauer muss zwischen 10 Minuten und 24 Stunden liegen.");
            try
            {
                await devices.SendCommandAsync(link, input.Command, input.DurationMinutes, ct);
                return Results.Accepted();
            }
            catch (DeviceProviderException ex)
            {
                return ApiResults.Error(ex.Message, StatusCodes.Status502BadGateway);
            }
        });

        device.MapPut("/cutting-height", async (int objectId, CuttingHeightInput input, BeetwerkDbContext db, DeviceService devices,
            DeviceStatusCache cache, CancellationToken ct) =>
        {
            var link = await db.DeviceLinks.FirstOrDefaultAsync(l => l.ObjectId == objectId, ct);
            if (link is null)
                return Results.NotFound();
            var status = cache.Get(link.Id)?.Status;
            var (min, max) = status is null ? (1, 9) : (status.CuttingHeightMin, status.CuttingHeightMax);
            if (input.Height < min || input.Height > max)
                return ApiResults.Error($"Die Schnitthöhe muss zwischen {min} und {max} liegen.");
            try
            {
                await devices.SetCuttingHeightAsync(link, input.Height, ct);
                return Results.Accepted();
            }
            catch (DeviceProviderException ex)
            {
                return ApiResults.Error(ex.Message, StatusCodes.Status502BadGateway);
            }
        });
    }

    private static DeviceLinkDto ToDto(DeviceLink link, CachedDeviceStatus? cached) =>
        new(link.ObjectId, link.Provider, link.ExternalId, link.Settings, link.CreateTasksOnError,
            cached?.Status, cached?.Error, cached is null || cached.FetchedAt == DateTimeOffset.MinValue ? null : cached.FetchedAt);
}
