using System.Security.Claims;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Api.Push;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;
using Kuestencode.Beetwerk.Domain.Geometry;

namespace Kuestencode.Beetwerk.Api.Endpoints;

public static class GardenEndpoints
{
    public static void MapGardenEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/config", (BeetwerkOptions options, VapidKeyStore vapid, ClaimsPrincipal user) => new
        {
            options.Version,
            AuthMode = options.AuthMode.ToString().ToLowerInvariant(),
            Username = user.Identity?.Name,
            options.PushEnabled,
            VapidPublicKey = options.PushEnabled ? vapid.Keys.PublicKey : null,
            Map = new
            {
                Primary = TileEndpoints.ToProxied(options.PrimaryTiles, "primary"),
                Fallback = options.FallbackTiles is null ? null : TileEndpoints.ToProxied(options.FallbackTiles, "fallback")
            }
        });

        api.MapGet("/garden", async (BeetwerkDbContext db, CancellationToken ct) =>
            (await db.Gardens.FindAsync([Garden.SingletonId], ct))!.ToDto());

        api.MapPut("/garden", async (GardenDto input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                return ApiResults.Error("Der Garten braucht einen Namen.");
            if (input.CenterLatitude is < -90 or > 90 || input.CenterLongitude is < -180 or > 180)
                return ApiResults.Error("Ungültiger Kartenmittelpunkt.");
            if (input.Zoom is < 0 or > 24)
                return ApiResults.Error("Ungültige Zoomstufe.");

            string? boundary = null;
            if (input.Boundary is { ValueKind: not System.Text.Json.JsonValueKind.Null })
            {
                var parsed = GeoJsonGeometry.Parse(input.Boundary);
                if (!parsed.IsValid)
                    return ApiResults.Error(parsed.Error!);
                if (parsed.Kind != GeometryKinds.Polygon)
                    return ApiResults.Error("Die Gartengrenze muss eine Fläche sein.");
                boundary = parsed.NormalizedJson;
            }

            var garden = (await db.Gardens.FindAsync([Garden.SingletonId], ct))!;
            garden.Name = input.Name.Trim();
            garden.CenterLatitude = input.CenterLatitude;
            garden.CenterLongitude = input.CenterLongitude;
            garden.Zoom = input.Zoom;
            garden.BoundaryGeoJson = boundary;
            await db.SaveChangesAsync(ct);
            return Results.Ok(garden.ToDto());
        });
    }
}
