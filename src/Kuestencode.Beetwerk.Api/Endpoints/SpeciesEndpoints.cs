using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Endpoints;

public static class SpeciesEndpoints
{
    public static void MapSpeciesEndpoints(this RouteGroupBuilder api)
    {
        var species = api.MapGroup("/species");

        species.MapGet("/", async (BeetwerkDbContext db, CancellationToken ct) =>
            (await db.PlantSpecies.OrderBy(s => s.Name).ToListAsync(ct)).Select(s => s.ToDto()));

        species.MapGet("/{id:int}", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var entity = await db.PlantSpecies.FindAsync([id], ct);
            if (entity is null)
                return Results.NotFound();

            var relations = await db.NeighborRelations
                .Include(r => r.SpeciesA)
                .Include(r => r.SpeciesB)
                .Where(r => r.SpeciesAId == id || r.SpeciesBId == id)
                .ToListAsync(ct);
            var neighbors = relations
                .Select(r =>
                {
                    var other = r.SpeciesAId == id ? r.SpeciesB! : r.SpeciesA!;
                    return new NeighborDto(r.Id, other.Id, other.Name, r.Rating, r.Note, r.Source);
                })
                .OrderBy(n => n.Rating)
                .ThenBy(n => n.SpeciesName)
                .ToList();
            var objectCount = await db.Objects.CountAsync(o => o.PlantSpeciesId == id, ct);
            return Results.Ok(new PlantSpeciesDetailDto(entity.ToDto(), neighbors, objectCount));
        });

        species.MapPost("/", async (PlantSpeciesInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var entity = new PlantSpecies { Name = "" };
            if (await ApplyAsync(entity, input, db, ct) is { } error)
                return error;
            db.PlantSpecies.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/species/{entity.Id}", entity.ToDto());
        });

        species.MapPut("/{id:int}", async (int id, PlantSpeciesInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var entity = await db.PlantSpecies.FindAsync([id], ct);
            if (entity is null)
                return Results.NotFound();
            if (await ApplyAsync(entity, input, db, ct) is { } error)
                return error;
            await db.SaveChangesAsync(ct);
            return Results.Ok(entity.ToDto());
        });

        species.MapDelete("/{id:int}", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var entity = await db.PlantSpecies.FindAsync([id], ct);
            if (entity is null)
                return Results.NotFound();
            db.PlantSpecies.Remove(entity);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var relations = api.MapGroup("/relations");

        relations.MapPost("/", async (NeighborRelationInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var relation = new NeighborRelation();
            if (await ApplyAsync(relation, input, db, ct) is { } error)
                return error;
            db.NeighborRelations.Add(relation);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/relations/{relation.Id}", new { relation.Id });
        });

        relations.MapPut("/{id:int}", async (int id, NeighborRelationInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var relation = await db.NeighborRelations.FindAsync([id], ct);
            if (relation is null)
                return Results.NotFound();
            if (await ApplyAsync(relation, input, db, ct) is { } error)
                return error;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { relation.Id });
        });

        relations.MapDelete("/{id:int}", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
            await db.NeighborRelations.Where(r => r.Id == id).ExecuteDeleteAsync(ct) > 0
                ? Results.NoContent()
                : Results.NotFound());
    }

    private static async Task<IResult?> ApplyAsync(PlantSpecies entity, PlantSpeciesInput input, BeetwerkDbContext db, CancellationToken ct)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return ApiResults.Error("Die Pflanzenart braucht einen Namen.");
        if (await db.PlantSpecies.AnyAsync(s => s.Id != entity.Id && s.Name == name, ct))
            return ApiResults.Conflict($"Die Pflanzenart '{name}' gibt es bereits.");

        var url = string.IsNullOrWhiteSpace(input.ExternalUrl) ? null : input.ExternalUrl.Trim();
        if (url is not null && !IsHttpUrl(url))
            return ApiResults.Error("Der externe Link muss mit http:// oder https:// beginnen.");

        entity.Name = name;
        entity.ScientificName = string.IsNullOrWhiteSpace(input.ScientificName) ? null : input.ScientificName.Trim();
        entity.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        entity.ExternalUrl = url;
        return null;
    }

    private static async Task<IResult?> ApplyAsync(NeighborRelation relation, NeighborRelationInput input, BeetwerkDbContext db, CancellationToken ct)
    {
        if (input.SpeciesId == input.OtherSpeciesId)
            return ApiResults.Error("Eine Art kann keine Beziehung zu sich selbst haben.");
        if (await db.PlantSpecies.CountAsync(s => s.Id == input.SpeciesId || s.Id == input.OtherSpeciesId, ct) != 2)
            return ApiResults.Error("Mindestens eine der Pflanzenarten existiert nicht.");
        if (!Enum.IsDefined(input.Rating))
            return ApiResults.Error("Ungültige Bewertung.");

        var (a, b) = NeighborRelation.Normalize(input.SpeciesId, input.OtherSpeciesId);
        if (await db.NeighborRelations.AnyAsync(r => r.Id != relation.Id && r.SpeciesAId == a && r.SpeciesBId == b, ct))
            return ApiResults.Conflict("Für dieses Artenpaar gibt es bereits einen Eintrag.");

        relation.SetPair(a, b);
        relation.Rating = input.Rating;
        relation.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        relation.Source = string.IsNullOrWhiteSpace(input.Source) ? null : input.Source.Trim();
        return null;
    }

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
