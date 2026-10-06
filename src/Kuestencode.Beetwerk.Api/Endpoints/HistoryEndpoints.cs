using System.Globalization;
using System.Security.Claims;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Api.Services;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Endpoints;

/// <summary>Verlauf (Notizen, Fotos, erledigte Aufgaben) und Fotos eines Objekts.</summary>
public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/objects/{objectId:int}/history", async (int objectId, BeetwerkDbContext db, CancellationToken ct) =>
        {
            if (!await db.Objects.AnyAsync(o => o.Id == objectId, ct))
                return Results.NotFound();
            var entries = await db.ObjectLog
                .Include(l => l.Photo)
                .Where(l => l.ObjectId == objectId)
                .OrderByDescending(l => l.Date)
                .ThenByDescending(l => l.Id)
                .ToListAsync(ct);
            return Results.Ok(entries.Select(ToDto));
        });

        api.MapPost("/objects/{objectId:int}/history", async (int objectId, HistoryNoteInput input, ClaimsPrincipal user,
            BeetwerkDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (!await db.Objects.AnyAsync(o => o.Id == objectId, ct))
                return Results.NotFound();
            var text = input.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return ApiResults.Error("Der Eintrag braucht einen Text.");

            var entry = new ObjectLogEntry
            {
                ObjectId = objectId,
                Date = input.Date ?? Today(time),
                Kind = ObjectLogKind.Note,
                Text = text[..Math.Min(text.Length, 4000)],
                CreatedBy = user.Identity?.Name,
                CreatedAt = time.GetUtcNow()
            };
            db.ObjectLog.Add(entry);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/history/{entry.Id}", ToDto(entry));
        });

        api.MapDelete("/history/{id:int}", async (int id, BeetwerkDbContext db, UploadStore uploads, CancellationToken ct) =>
        {
            var entry = await db.ObjectLog.Include(l => l.Photo).FirstOrDefaultAsync(l => l.Id == id, ct);
            if (entry is null)
                return Results.NotFound();
            // Ein Foto-Eintrag ist das Foto selbst: Mit dem Eintrag verschwindet auch die Datei.
            if (entry.Photo is { } photo)
            {
                db.ObjectPhotos.Remove(photo);
                await db.SaveChangesAsync(ct);
                uploads.Delete(photo.FileName, photo.ThumbnailFileName);
            }
            else
            {
                db.ObjectLog.Remove(entry);
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });

        api.MapPost("/objects/{objectId:int}/photos", async (int objectId, HttpRequest request, ClaimsPrincipal user,
                BeetwerkDbContext db, UploadStore uploads, TimeProvider time, CancellationToken ct) =>
            {
                if (!await db.Objects.AnyAsync(o => o.Id == objectId, ct))
                    return Results.NotFound();
                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                var thumbnail = form.Files.GetFile("thumbnail");
                if (file is null || thumbnail is null)
                    return ApiResults.Error("Foto und Vorschaubild werden benötigt.");

                var takenOn = DateOnly.TryParseExact(form["takenOn"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : Today(time);
                var caption = form["caption"].ToString().Trim();

                (string FileName, string ContentType) image, thumb;
                try
                {
                    image = await uploads.SaveImageAsync(file, "photos", ct);
                    thumb = await uploads.SaveImageAsync(thumbnail, "photos", ct);
                }
                catch (InvalidDataException ex)
                {
                    return ApiResults.Error(ex.Message);
                }

                var photo = new ObjectPhoto
                {
                    ObjectId = objectId,
                    FileName = image.FileName,
                    ThumbnailFileName = thumb.FileName,
                    ContentType = image.ContentType,
                    Caption = caption.Length > 0 ? caption[..Math.Min(caption.Length, 500)] : null,
                    TakenOn = takenOn,
                    CreatedBy = user.Identity?.Name,
                    CreatedAt = time.GetUtcNow()
                };
                var entry = new ObjectLogEntry
                {
                    ObjectId = objectId,
                    Date = takenOn,
                    Kind = ObjectLogKind.Photo,
                    Text = photo.Caption ?? "Foto",
                    Photo = photo,
                    CreatedBy = photo.CreatedBy,
                    CreatedAt = photo.CreatedAt
                };
                db.ObjectLog.Add(entry);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/history/{entry.Id}", ToDto(entry));
            })
            .DisableAntiforgery();

        api.MapGet("/photos/{id:int}/{variant:regex(^(image|thumbnail)$)}", async (int id, string variant, BeetwerkDbContext db,
            UploadStore uploads, HttpContext context, CancellationToken ct) =>
        {
            var photo = await db.ObjectPhotos.FindAsync([id], ct);
            if (photo is null)
                return Results.NotFound();
            // Fotos ändern sich nie, neue bekommen neue Ids.
            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return uploads.Serve(variant == "image" ? photo.FileName : photo.ThumbnailFileName, photo.ContentType);
        });
    }

    /// <summary>Hält im Verlauf fest, dass eine Aufgabe an einem Objekt erledigt wurde.</summary>
    public static void LogCompletion(BeetwerkDbContext db, GardenTask task, string? username, DateOnly today, DateTimeOffset now)
    {
        if (task.ObjectId is not { } objectId)
            return;
        db.ObjectLog.Add(new ObjectLogEntry
        {
            ObjectId = objectId,
            Date = today,
            Kind = ObjectLogKind.TaskCompleted,
            Text = task.Title,
            TaskId = task.Id,
            CreatedBy = username,
            CreatedAt = now
        });
    }

    private static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    private static HistoryEntryDto ToDto(ObjectLogEntry entry) =>
        new(entry.Id, entry.Date, entry.Kind, entry.Text, entry.CreatedBy, entry.TaskId,
            entry.Photo is { } photo
                ? new PhotoDto(photo.Id, $"/api/photos/{photo.Id}/image", $"/api/photos/{photo.Id}/thumbnail", photo.Caption)
                : null);
}
