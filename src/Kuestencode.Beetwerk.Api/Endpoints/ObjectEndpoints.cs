using System.Globalization;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;
using Kuestencode.Beetwerk.Domain.Geometry;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Endpoints;

public static class ObjectEndpoints
{
    public static void MapObjectEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/objects");

        group.MapGet("/", async (BeetwerkDbContext db, CancellationToken ct) =>
            (await db.Objects.Include(o => o.PlantSpecies).OrderBy(o => o.Name).ToListAsync(ct)).Select(o => o.ToDto()));

        group.MapGet("/{id:int}", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
            await db.Objects.Include(o => o.PlantSpecies).FirstOrDefaultAsync(o => o.Id == id, ct) is { } obj
                ? Results.Ok(obj.ToDto())
                : Results.NotFound());

        group.MapPost("/", async (GardenObjectInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var obj = new GardenObject { Name = "", GeometryGeoJson = "" };
            if (await ApplyAsync(obj, input, db, ct) is { } error)
                return error;
            db.Objects.Add(obj);
            await db.SaveChangesAsync(ct);
            await db.Entry(obj).Reference(o => o.PlantSpecies).LoadAsync(ct);
            return Results.Created($"/api/objects/{obj.Id}", obj.ToDto());
        });

        group.MapPut("/{id:int}", async (int id, GardenObjectInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var obj = await db.Objects.FindAsync([id], ct);
            if (obj is null)
                return Results.NotFound();
            if (await ApplyAsync(obj, input, db, ct) is { } error)
                return error;
            await db.SaveChangesAsync(ct);
            await db.Entry(obj).Reference(o => o.PlantSpecies).LoadAsync(ct);
            return Results.Ok(obj.ToDto());
        });

        group.MapDelete("/{id:int}", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var obj = await db.Objects.FindAsync([id], ct);
            if (obj is null)
                return Results.NotFound();
            // Aufgaben am Objekt werden per Kaskade gelöscht, enthaltene Objekte verlieren nur ihre Zuordnung.
            await db.Objects.Where(o => o.ParentObjectId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ParentObjectId, (int?)null), ct);
            db.Objects.Remove(obj);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<IResult?> ApplyAsync(GardenObject obj, GardenObjectInput input, BeetwerkDbContext db, CancellationToken ct)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return ApiResults.Error("Das Objekt braucht einen Namen.");

        var type = await db.ObjectTypes.FindAsync([input.ObjectTypeId], ct);
        if (type is null)
            return ApiResults.Error("Unbekannter Objekttyp.");

        var geometry = GeoJsonGeometry.Parse(input.Geometry);
        if (!geometry.IsValid)
            return ApiResults.Error(geometry.Error!);
        if (!type.Allows(geometry.Kind))
            return ApiResults.Error($"Der Typ '{type.Name}' erlaubt keine Geometrie vom Typ {geometry.Kind}.");

        if (input.ParentObjectId is { } parentId)
        {
            if (parentId == obj.Id)
                return ApiResults.Error("Ein Objekt kann nicht in sich selbst liegen.");
            if (!await db.Objects.AnyAsync(o => o.Id == parentId, ct))
                return ApiResults.Error("Das übergeordnete Objekt existiert nicht.");
            if (obj.Id != 0 && await IsDescendantAsync(parentId, obj.Id, db, ct))
                return ApiResults.Error("Das übergeordnete Objekt liegt selbst in diesem Objekt.");
        }

        var hasSpeciesField = type.Fields.Any(f => f.Type == ObjectFieldType.Species);
        if (input.PlantSpeciesId is { } speciesId)
        {
            if (!hasSpeciesField)
                return ApiResults.Error($"Der Typ '{type.Name}' hat kein Pflanzenart-Feld.");
            if (!await db.PlantSpecies.AnyAsync(s => s.Id == speciesId, ct))
                return ApiResults.Error("Die Pflanzenart existiert nicht.");
        }

        var attributes = new Dictionary<string, string>();
        foreach (var (key, value) in input.Attributes ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            var field = type.Fields.FirstOrDefault(f => f.Key == key && f.Type != ObjectFieldType.Species);
            if (field is null)
                return ApiResults.Error($"Das Feld '{key}' gibt es beim Typ '{type.Name}' nicht.");
            if (field.Type == ObjectFieldType.Date &&
                !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return ApiResults.Error($"'{field.Label}' muss ein Datum im Format JJJJ-MM-TT sein.");
            attributes[key] = value.Trim();
        }

        obj.Name = name;
        obj.ObjectTypeId = type.Id;
        obj.GeometryGeoJson = geometry.NormalizedJson!;
        obj.GeometryKind = geometry.Kind;
        obj.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        obj.ParentObjectId = input.ParentObjectId;
        obj.PlantSpeciesId = input.PlantSpeciesId;
        obj.Attributes = attributes;
        return null;
    }

    private static async Task<bool> IsDescendantAsync(int candidateId, int ancestorId, BeetwerkDbContext db, CancellationToken ct)
    {
        var parents = await db.Objects.Select(o => new { o.Id, o.ParentObjectId }).ToDictionaryAsync(o => o.Id, o => o.ParentObjectId, ct);
        var visited = new HashSet<int>();
        for (int? current = candidateId; current is { } id && visited.Add(id); current = parents.GetValueOrDefault(id))
        {
            if (id == ancestorId)
                return true;
        }
        return false;
    }
}
