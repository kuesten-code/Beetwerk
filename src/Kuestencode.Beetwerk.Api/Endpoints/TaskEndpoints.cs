using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Api.Services;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;
using Kuestencode.Beetwerk.Domain.Geometry;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Endpoints;

public static class TaskEndpoints
{
    public const int MaxLeadDays = 365;

    public static void MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tasks");

        group.MapGet("/", async (string? status, int? objectId, DateOnly? from, DateOnly? to, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var query = db.Tasks.Include(t => t.Object).AsQueryable();
            query = status switch
            {
                "done" => query.Where(t => t.Status == GardenTaskStatus.Done),
                "all" => query,
                _ => query.Where(t => t.Status == GardenTaskStatus.Open)
            };
            if (objectId is { } oid)
                query = query.Where(t => t.ObjectId == oid);
            if (from is { } f)
                query = query.Where(t => t.DueDate >= f);
            if (to is { } t2)
                query = query.Where(t => t.DueDate <= t2);
            var tasks = await query.OrderBy(t => t.DueDate).ThenBy(t => t.Title).ToListAsync(ct);
            return tasks.Select(t => t.ToDto());
        });

        group.MapGet("/calendar", async (int? year, bool? includeDone, BeetwerkDbContext db, BeetwerkOptions options,
            TimeProvider time, HttpContext context, CancellationToken ct) =>
        {
            var exportYear = year ?? time.GetLocalNow().Year;
            if (exportYear is < 2000 or > 2100)
                return ApiResults.Error("Ungültiges Jahr.");

            var garden = (await db.Gardens.FindAsync([Garden.SingletonId], ct))!;
            var tasks = await db.Tasks.Include(t => t.Object).ToListAsync(ct);
            var ics = CalendarExporter.Export(tasks,
                new CalendarExporter.Options(exportYear, includeDone ?? false, garden.Name, options.NotifyHour, time.GetUtcNow()));

            context.Response.Headers.ContentDisposition = $"attachment; filename=\"beetwerk-{exportYear}.ics\"";
            return Results.Text(ics, "text/calendar; charset=utf-8");
        });

        group.MapGet("/{id:int}", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
            await db.Tasks.Include(t => t.Object).FirstOrDefaultAsync(t => t.Id == id, ct) is { } task
                ? Results.Ok(task.ToDto())
                : Results.NotFound());

        group.MapPost("/", async (GardenTaskInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var task = new GardenTask { Title = "" };
            if (await ApplyAsync(task, input, db, ct) is { } error)
                return error;
            db.Tasks.Add(task);
            await db.SaveChangesAsync(ct);
            await db.Entry(task).Reference(t => t.Object).LoadAsync(ct);
            return Results.Created($"/api/tasks/{task.Id}", task.ToDto());
        });

        group.MapPut("/{id:int}", async (int id, GardenTaskInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var task = await db.Tasks.FindAsync([id], ct);
            if (task is null)
                return Results.NotFound();
            if (await ApplyAsync(task, input, db, ct) is { } error)
                return error;
            await db.SaveChangesAsync(ct);
            await db.Entry(task).Reference(t => t.Object).LoadAsync(ct);
            return Results.Ok(task.ToDto());
        });

        group.MapPost("/{id:int}/complete", async (int id, System.Security.Claims.ClaimsPrincipal user, BeetwerkDbContext db,
            TimeProvider time, CancellationToken ct) =>
        {
            var task = await db.Tasks.Include(t => t.Object).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null)
                return Results.NotFound();
            if (task.Status == GardenTaskStatus.Done)
                return ApiResults.Conflict("Die Aufgabe ist bereits erledigt.");

            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var next = task.Complete(time.GetUtcNow(), today);
            if (next is not null)
                db.Tasks.Add(next);
            HistoryEndpoints.LogCompletion(db, task, user.Identity?.Name, today, time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            if (next is not null)
                await db.Entry(next).Reference(t => t.Object).LoadAsync(ct);
            return Results.Ok(new CompleteTaskResult(task.ToDto(), next?.ToDto()));
        });

        group.MapPost("/{id:int}/reopen", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var task = await db.Tasks.Include(t => t.Object).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null)
                return Results.NotFound();
            if (task.Status == GardenTaskStatus.Open)
                return ApiResults.Conflict("Die Aufgabe ist bereits offen.");

            // Die beim Erledigen erzeugte Folgeinstanz zurücknehmen, sonst gäbe es den Termin doppelt.
            await db.Tasks
                .Where(t => t.GeneratedFromTaskId == id && t.Status == GardenTaskStatus.Open)
                .ExecuteDeleteAsync(ct);
            await db.ObjectLog
                .Where(l => l.TaskId == id && l.Kind == ObjectLogKind.TaskCompleted)
                .ExecuteDeleteAsync(ct);
            task.Reopen();
            await db.SaveChangesAsync(ct);
            return Results.Ok(task.ToDto());
        });

        group.MapDelete("/{id:int}", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
            await db.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync(ct) > 0
                ? Results.NoContent()
                : Results.NotFound());
    }

    private static async Task<IResult?> ApplyAsync(GardenTask task, GardenTaskInput input, BeetwerkDbContext db, CancellationToken ct)
    {
        var title = input.Title?.Trim();
        if (string.IsNullOrEmpty(title))
            return ApiResults.Error("Die Aufgabe braucht einen Titel.");
        if (input.LeadDays is < 0 or > MaxLeadDays)
            return ApiResults.Error($"Der Vorlauf muss zwischen 0 und {MaxLeadDays} Tagen liegen.");
        if (!Enum.IsDefined(input.Frequency))
            return ApiResults.Error("Ungültige Wiederholung.");

        string? geometry = null;
        if (input.ObjectId is { } objectId)
        {
            if (!await db.Objects.AnyAsync(o => o.Id == objectId, ct))
                return ApiResults.Error("Das Objekt existiert nicht.");
        }
        else
        {
            var parsed = GeoJsonGeometry.Parse(input.Geometry);
            if (!parsed.IsValid)
                return ApiResults.Error("Eine Aufgabe ohne Objekt braucht einen Ort auf der Karte: " + parsed.Error);
            geometry = parsed.NormalizedJson;
        }

        var recurring = input.Frequency != RecurrenceFrequency.None;
        var interval = recurring ? input.Interval ?? 1 : 1;
        var seasonStart = recurring ? input.SeasonStartMonth : null;
        var seasonEnd = recurring ? input.SeasonEndMonth : null;
        if (recurring)
        {
            var rule = new Domain.Recurrence.RecurrenceRule(input.Frequency, interval, input.DueDate, seasonStart, seasonEnd);
            if (rule.Validate() is { } error)
                return ApiResults.Error(error);
        }

        if (task.DueDate != input.DueDate || task.LeadDays != input.LeadDays)
            task.NotifiedOn = null;
        // Neuer Termin oder neuer Rhythmus verankert die Serie am Fälligkeitsdatum (wie ein neues DTSTART).
        if (task.Id == 0 || task.DueDate != input.DueDate || task.Frequency != input.Frequency || task.Interval != interval)
            task.SeriesStart = input.DueDate;

        task.Title = title;
        task.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        task.ObjectId = input.ObjectId;
        task.GeometryGeoJson = geometry;
        task.DueDate = input.DueDate;
        task.Notify = input.Notify;
        task.LeadDays = input.LeadDays;
        task.Frequency = input.Frequency;
        task.Interval = interval;
        task.SeasonStartMonth = seasonStart;
        task.SeasonEndMonth = seasonEnd;
        return null;
    }
}
