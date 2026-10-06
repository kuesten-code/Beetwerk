using System.Globalization;
using System.Text.Json;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Api.Services;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Endpoints;

public static class OverlayEndpoints
{
    public static void MapOverlayEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/overlays");

        group.MapGet("/", async (BeetwerkDbContext db, CancellationToken ct) =>
            (await db.MapOverlays.OrderBy(o => o.Id).ToListAsync(ct)).Select(ToDto));

        group.MapPost("/", async (HttpRequest request, BeetwerkDbContext db, UploadStore uploads, TimeProvider time, CancellationToken ct) =>
            {
                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                if (file is null)
                    return ApiResults.Error("Es wurde kein Bild übertragen.");
                if (ParseCorners(form["corners"]) is not { } corners)
                    return ApiResults.Error("Ungültige Eckpunkte.");
                if (!int.TryParse(form["width"], out var width) || !int.TryParse(form["height"], out var height) || width <= 0 || height <= 0)
                    return ApiResults.Error("Bildgröße fehlt.");

                (string FileName, string ContentType) stored;
                try
                {
                    stored = await uploads.SaveImageAsync(file, "overlays", ct);
                }
                catch (InvalidDataException ex)
                {
                    return ApiResults.Error(ex.Message);
                }

                var name = form["name"].ToString().Trim();
                var overlay = new MapOverlay
                {
                    Name = name.Length > 0 ? name[..Math.Min(name.Length, 200)] : "Eigenes Luftbild",
                    FileName = stored.FileName,
                    ContentType = stored.ContentType,
                    Width = width,
                    Height = height,
                    CornersJson = corners,
                    UpdatedAt = time.GetUtcNow()
                };
                db.MapOverlays.Add(overlay);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/overlays/{overlay.Id}", ToDto(overlay));
            })
            .DisableAntiforgery();

        group.MapPut("/{id:int}", async (int id, OverlayInput input, BeetwerkDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var overlay = await db.MapOverlays.FindAsync([id], ct);
            if (overlay is null)
                return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name))
                return ApiResults.Error("Das Luftbild braucht einen Namen.");
            if (ParseCorners(JsonSerializer.Serialize(input.Corners)) is not { } corners)
                return ApiResults.Error("Ungültige Eckpunkte.");
            if (input.Opacity is < 0 or > 1)
                return ApiResults.Error("Die Deckkraft muss zwischen 0 und 1 liegen.");

            overlay.Name = input.Name.Trim();
            overlay.CornersJson = corners;
            overlay.Opacity = input.Opacity;
            overlay.Visible = input.Visible;
            overlay.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(overlay));
        });

        group.MapDelete("/{id:int}", async (int id, BeetwerkDbContext db, UploadStore uploads, CancellationToken ct) =>
        {
            var overlay = await db.MapOverlays.FindAsync([id], ct);
            if (overlay is null)
                return Results.NotFound();
            db.MapOverlays.Remove(overlay);
            await db.SaveChangesAsync(ct);
            uploads.Delete(overlay.FileName);
            return Results.NoContent();
        });

        group.MapGet("/{id:int}/image", async (int id, BeetwerkDbContext db, UploadStore uploads, HttpContext context, CancellationToken ct) =>
        {
            var overlay = await db.MapOverlays.FindAsync([id], ct);
            if (overlay is null)
                return Results.NotFound();
            // Die URL enthält eine Versionsnummer, daher darf der Browser das Bild lange behalten.
            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return uploads.Serve(overlay.FileName, overlay.ContentType);
        });
    }

    /// <summary>Normalisiert vier [lng, lat]-Paare zu JSON oder liefert null bei ungültigen Werten.</summary>
    public static string? ParseCorners(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var corners = JsonSerializer.Deserialize<double[][]>(json);
            if (corners is not { Length: 4 } || corners.Any(c => c is not { Length: 2 } || c[0] is < -180 or > 180 || c[1] is < -90 or > 90))
                return null;
            return JsonSerializer.Serialize(corners);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OverlayDto ToDto(MapOverlay overlay) =>
        new(overlay.Id, overlay.Name, overlay.Width, overlay.Height,
            JsonSerializer.Deserialize<double[][]>(overlay.CornersJson)!, overlay.Opacity, overlay.Visible,
            $"/api/overlays/{overlay.Id}/image?v={overlay.UpdatedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}");
}
