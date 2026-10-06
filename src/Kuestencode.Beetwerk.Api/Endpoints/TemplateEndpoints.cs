using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Enums;
using Kuestencode.Beetwerk.Domain.Templates;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Endpoints;

/// <summary>Pflanzenvorlagen: typische Aufgaben einer Art, die beim Anlegen einer Pflanze übernommen werden können.</summary>
public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/species/{speciesId:int}/templates", async (int speciesId, TaskTemplateInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            if (!await db.PlantSpecies.AnyAsync(s => s.Id == speciesId, ct))
                return Results.NotFound();
            var template = new SpeciesTaskTemplate { PlantSpeciesId = speciesId, Title = "" };
            if (Apply(template, input) is { } error)
                return ApiResults.Error(error);
            db.SpeciesTaskTemplates.Add(template);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/templates/{template.Id}", ToDto(template));
        });

        api.MapPut("/templates/{id:int}", async (int id, TaskTemplateInput input, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var template = await db.SpeciesTaskTemplates.FindAsync([id], ct);
            if (template is null)
                return Results.NotFound();
            if (Apply(template, input) is { } error)
                return ApiResults.Error(error);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(template));
        });

        api.MapDelete("/templates/{id:int}", async (int id, BeetwerkDbContext db, CancellationToken ct) =>
            await db.SpeciesTaskTemplates.Where(t => t.Id == id).ExecuteDeleteAsync(ct) > 0
                ? Results.NoContent()
                : Results.NotFound());

        api.MapPost("/objects/{objectId:int}/apply-templates", async (int objectId, ApplyTemplatesInput input,
            BeetwerkDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var obj = await db.Objects.FindAsync([objectId], ct);
            if (obj is null)
                return Results.NotFound();
            if (obj.PlantSpeciesId is null)
                return ApiResults.Error("Das Objekt hat keine Pflanzenart.");

            var ids = input.TemplateIds?.Distinct().ToList() ?? [];
            var templates = await db.SpeciesTaskTemplates
                .Where(t => t.PlantSpeciesId == obj.PlantSpeciesId && ids.Contains(t.Id))
                .ToListAsync(ct);
            if (templates.Count != ids.Count)
                return ApiResults.Error("Mindestens eine Vorlage gehört nicht zur Pflanzenart des Objekts.");

            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var tasks = templates.Select(t => t.CreateTask(objectId, today)).ToList();
            db.Tasks.AddRange(tasks);
            await db.SaveChangesAsync(ct);
            return Results.Ok(tasks.Select(t => t.ToDto()));
        });
    }

    public static TaskTemplateDto ToDto(SpeciesTaskTemplate t) =>
        new(t.Id, t.Title, t.Description, t.Frequency, t.Interval, t.SeasonStartMonth, t.SeasonEndMonth, t.StartMonth, t.StartDay, t.Notify, t.LeadDays);

    private static string? Apply(SpeciesTaskTemplate template, TaskTemplateInput input)
    {
        if (!Enum.IsDefined(input.Frequency))
            return "Ungültige Wiederholung.";
        var recurring = input.Frequency != RecurrenceFrequency.None;
        var useSeason = recurring && input.Frequency != RecurrenceFrequency.Yearly;

        template.Title = input.Title?.Trim() ?? "";
        template.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        template.Frequency = input.Frequency;
        template.Interval = recurring ? input.Interval ?? 1 : 1;
        template.SeasonStartMonth = useSeason ? input.SeasonStartMonth : null;
        template.SeasonEndMonth = useSeason ? input.SeasonEndMonth : null;
        template.StartMonth = input.StartMonth;
        template.StartDay = input.StartDay ?? 1;
        template.Notify = input.Notify;
        template.LeadDays = input.LeadDays;
        return template.Validate();
    }
}
