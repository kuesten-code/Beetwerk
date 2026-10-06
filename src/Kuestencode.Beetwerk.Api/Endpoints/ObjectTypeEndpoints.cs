using System.Text.RegularExpressions;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Endpoints;

public static partial class ObjectTypeEndpoints
{
    public static void MapObjectTypeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/object-types");

        group.MapGet("/", async (BeetwerkDbContext db, CancellationToken ct) =>
            (await db.ObjectTypes.OrderBy(t => t.Name).ToListAsync(ct)).Select(t => t.ToDto()));

        group.MapPost("/", async (ObjectTypeInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var type = new ObjectType { Name = "" };
            if (await ApplyAsync(type, input, db, ct) is { } error)
                return error;
            db.ObjectTypes.Add(type);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/object-types/{type.Id}", type.ToDto());
        });

        group.MapPut("/{id:int}", async (int id, ObjectTypeInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var type = await db.ObjectTypes.FindAsync([id], ct);
            if (type is null)
                return Results.NotFound();
            if (await ApplyAsync(type, input, db, ct) is { } error)
                return error;
            await db.SaveChangesAsync(ct);
            return Results.Ok(type.ToDto());
        });

        group.MapDelete("/{id:int}", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var type = await db.ObjectTypes.FindAsync([id], ct);
            if (type is null)
                return Results.NotFound();
            var usage = await db.Objects.CountAsync(o => o.ObjectTypeId == id, ct);
            if (usage > 0)
                return ApiResults.Conflict($"Der Typ wird noch von {usage} Objekt(en) verwendet.");
            db.ObjectTypes.Remove(type);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<IResult?> ApplyAsync(ObjectType type, ObjectTypeInput input, BeetwerkDbContext db, CancellationToken ct)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return ApiResults.Error("Der Typ braucht einen Namen.");
        if (await db.ObjectTypes.AnyAsync(t => t.Id != type.Id && t.Name == name, ct))
            return ApiResults.Conflict($"Es gibt bereits einen Typ '{name}'.");

        var color = input.Color?.Trim() ?? type.Color;
        if (!ColorPattern().IsMatch(color))
            return ApiResults.Error("Die Farbe muss als Hexwert angegeben werden, z. B. #4caf50.");

        var geometries = GeometryKinds.None;
        foreach (var geometryName in input.AllowedGeometries ?? [])
        {
            if (!Enum.TryParse<GeometryKinds>(geometryName, out var kind) || kind is GeometryKinds.None or GeometryKinds.All)
                return ApiResults.Error($"Unbekannte Geometrie '{geometryName}'.");
            geometries |= kind;
        }
        if (geometries == GeometryKinds.None)
            return ApiResults.Error("Mindestens eine Geometrie muss erlaubt sein.");

        if (type.Id != 0)
        {
            var removed = type.AllowedGeometries & ~geometries;
            var inUse = await db.Objects
                .Where(o => o.ObjectTypeId == type.Id)
                .Select(o => o.GeometryKind)
                .Distinct()
                .ToListAsync(ct);
            if (inUse.Any(k => (removed & k) != 0))
                return ApiResults.Conflict("Bestehende Objekte dieses Typs nutzen eine Geometrie, die entfernt werden soll.");
        }

        var fields = (input.Fields ?? []).Select(f => new ObjectTypeField(f.Key?.Trim() ?? "", f.Label?.Trim() ?? "", f.Type)).ToList();
        if (fields.Any(f => !FieldKeyPattern().IsMatch(f.Key) || f.Label.Length == 0))
            return ApiResults.Error("Jedes Zusatzfeld braucht einen Schlüssel (Buchstaben/Ziffern) und eine Bezeichnung.");
        if (fields.Select(f => f.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Count)
            return ApiResults.Error("Die Schlüssel der Zusatzfelder müssen eindeutig sein.");
        if (fields.Count(f => f.Type == ObjectFieldType.Species) > 1)
            return ApiResults.Error("Ein Typ kann höchstens ein Pflanzenart-Feld haben.");

        type.Name = name;
        type.Icon = string.IsNullOrWhiteSpace(input.Icon) ? type.Icon : input.Icon.Trim();
        type.Color = color;
        type.AllowedGeometries = geometries;
        type.Fields = fields;
        return null;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,39}$")]
    private static partial Regex FieldKeyPattern();
}
