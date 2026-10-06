using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Tests.Api;

public class GardenApiTests : IAsyncLifetime
{
    private readonly BeetwerkFactory factory = new();
    private HttpClient client = null!;

    public async Task InitializeAsync() => client = await factory.CreateLoggedInClientAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    private static JsonElement Geo(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static readonly JsonElement Point = Geo("""{"type":"Point","coordinates":[10.13,54.32]}""");
    private static readonly JsonElement Square = Geo("""{"type":"Polygon","coordinates":[[[10.13,54.32],[10.131,54.32],[10.131,54.321],[10.13,54.32]]]}""");

    private async Task<ObjectTypeDto> Type(string name) =>
        (await (await client.GetAsync("/api/object-types")).ReadAsync<List<ObjectTypeDto>>()).Single(t => t.Name == name);

    private async Task<GardenObjectDto> CreateObject(string name, string typeName, JsonElement geometry, int? speciesId = null,
        Dictionary<string, string>? attributes = null, int? parentId = null) =>
        await (await client.PostAsJsonAsync("/api/objects",
            new GardenObjectInput(name, (await Type(typeName)).Id, geometry, null, parentId, speciesId, attributes))).ReadAsync<GardenObjectDto>();

    private static GardenTaskInput NewTask(string title, DateOnly due, int? objectId = null, JsonElement? geometry = null,
        RecurrenceFrequency frequency = RecurrenceFrequency.None, int? from = null, int? to = null) =>
        new(title, null, objectId, geometry, due, true, 0, frequency, 1, from, to);

    [Fact]
    public async Task Default_object_types_are_seeded()
    {
        var types = await (await client.GetAsync("/api/object-types")).ReadAsync<List<ObjectTypeDto>>();
        Assert.Equal(7, types.Count);
    }

    [Fact]
    public async Task Garden_settings_can_be_updated()
    {
        var garden = await (await client.PutAsJsonAsync("/api/garden", new GardenDto("Schrebergarten", 54.1, 10.2, 18.5, Square)))
            .ReadAsync<GardenDto>();
        Assert.Equal("Schrebergarten", garden.Name);
        Assert.Equal("Polygon", garden.Boundary!.Value.GetProperty("type").GetString());

        var invalid = await client.PutAsJsonAsync("/api/garden", new GardenDto("x", 54.1, 10.2, 18, Point));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Object_with_disallowed_geometry_is_rejected()
    {
        var response = await client.PostAsJsonAsync("/api/objects",
            new GardenObjectInput("Hochbeet", (await Type("Beet")).Id, Point, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Plant_with_species_parent_and_attributes_roundtrips()
    {
        var species = await (await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("Tomate", "Solanum lycopersicum", null, "https://www.naturadb.de/")))
            .ReadAsync<PlantSpeciesDto>();
        var bed = await CreateObject("Hochbeet", "Beet", Square);
        var plant = await CreateObject("Tomate 1", "Pflanze", Point, species.Id,
            new Dictionary<string, string> { ["variety"] = "Ochsenherz", ["plantedOn"] = "2026-05-01" }, bed.Id);

        Assert.Equal("Tomate", plant.PlantSpeciesName);
        Assert.Equal(bed.Id, plant.ParentObjectId);
        Assert.Equal("Ochsenherz", plant.Attributes["variety"]);
        Assert.Equal("Point", plant.GeometryKind);
    }

    [Fact]
    public async Task Unknown_attribute_and_bad_date_are_rejected()
    {
        var typeId = (await Type("Pflanze")).Id;
        var unknown = await client.PostAsJsonAsync("/api/objects",
            new GardenObjectInput("x", typeId, Point, null, null, null, new() { ["foo"] = "bar" }));
        var badDate = await client.PostAsJsonAsync("/api/objects",
            new GardenObjectInput("x", typeId, Point, null, null, null, new() { ["plantedOn"] = "01.05.2026" }));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
    }

    [Fact]
    public async Task Species_cannot_be_set_on_type_without_species_field()
    {
        var species = await (await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("Möhre", null, null, null))).ReadAsync<PlantSpeciesDto>();
        var response = await client.PostAsJsonAsync("/api/objects",
            new GardenObjectInput("Stall", (await Type("Stall")).Id, Square, null, null, species.Id, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Parent_cycles_are_rejected()
    {
        var outer = await CreateObject("Außen", "Sonstiges", Square);
        var inner = await CreateObject("Innen", "Sonstiges", Square, parentId: outer.Id);
        var response = await client.PutAsJsonAsync($"/api/objects/{outer.Id}",
            new GardenObjectInput("Außen", outer.ObjectTypeId, Square, null, inner.Id, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_object_deletes_its_tasks_and_detaches_children()
    {
        var bed = await CreateObject("Beet", "Beet", Square);
        var plant = await CreateObject("Salat", "Pflanze", Point, parentId: bed.Id);
        await client.PostAsJsonAsync("/api/tasks", NewTask("Unkraut", new DateOnly(2026, 10, 8), bed.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/objects/{bed.Id}")).StatusCode);

        Assert.Empty(await (await client.GetAsync("/api/tasks?status=all")).ReadAsync<List<GardenTaskDto>>());
        var detached = await (await client.GetAsync($"/api/objects/{plant.Id}")).ReadAsync<GardenObjectDto>();
        Assert.Null(detached.ParentObjectId);
    }

    [Fact]
    public async Task Object_type_in_use_cannot_be_deleted_or_lose_used_geometry()
    {
        var type = await Type("Sonstiges");
        await CreateObject("Kompost", "Sonstiges", Point);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/object-types/{type.Id}")).StatusCode);
        var shrink = await client.PutAsJsonAsync($"/api/object-types/{type.Id}",
            new ObjectTypeInput(type.Name, type.Icon, type.Color, ["Polygon"], []));
        Assert.Equal(HttpStatusCode.Conflict, shrink.StatusCode);
    }

    [Fact]
    public async Task Custom_object_type_validation()
    {
        var ok = await client.PostAsJsonAsync("/api/object-types", new ObjectTypeInput("Teich", "💧", "#2196f3", ["Polygon"],
            [new ObjectTypeFieldDto("depth", "Tiefe", ObjectFieldType.Text)]));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/object-types", new ObjectTypeInput("teich", null, null, ["Point"], null));
        var noGeometry = await client.PostAsJsonAsync("/api/object-types", new ObjectTypeInput("Leer", null, null, [], null));
        var badColor = await client.PostAsJsonAsync("/api/object-types", new ObjectTypeInput("Rot", null, "red", ["Point"], null));
        var twoSpecies = await client.PostAsJsonAsync("/api/object-types", new ObjectTypeInput("Doppel", null, null, ["Point"],
            [new("a", "A", ObjectFieldType.Species), new("b", "B", ObjectFieldType.Species)]));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noGeometry.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badColor.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, twoSpecies.StatusCode);
    }

    [Fact]
    public async Task Neighbor_relations_are_symmetric_and_unique_per_pair()
    {
        var tomato = await (await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("Tomate", null, null, null))).ReadAsync<PlantSpeciesDto>();
        var basil = await (await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("Basilikum", null, null, null))).ReadAsync<PlantSpeciesDto>();

        var created = await client.PostAsJsonAsync("/api/relations",
            new NeighborRelationInput(basil.Id, tomato.Id, NeighborRating.Good, "passt", "NaturaDB-Artikel Mischkultur im Hochbeet"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var reversed = await client.PostAsJsonAsync("/api/relations", new NeighborRelationInput(tomato.Id, basil.Id, NeighborRating.Bad, null, null));
        Assert.Equal(HttpStatusCode.Conflict, reversed.StatusCode);

        var fromTomato = await (await client.GetAsync($"/api/species/{tomato.Id}")).ReadAsync<PlantSpeciesDetailDto>();
        var fromBasil = await (await client.GetAsync($"/api/species/{basil.Id}")).ReadAsync<PlantSpeciesDetailDto>();
        Assert.Equal("Basilikum", Assert.Single(fromTomato.Neighbors).SpeciesName);
        Assert.Equal("Tomate", Assert.Single(fromBasil.Neighbors).SpeciesName);
        Assert.Equal("NaturaDB-Artikel Mischkultur im Hochbeet", fromBasil.Neighbors[0].Source);

        var self = await client.PostAsJsonAsync("/api/relations", new NeighborRelationInput(tomato.Id, tomato.Id, NeighborRating.Good, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);

        await client.DeleteAsync($"/api/species/{basil.Id}");
        Assert.Empty((await (await client.GetAsync($"/api/species/{tomato.Id}")).ReadAsync<PlantSpeciesDetailDto>()).Neighbors);
    }

    [Fact]
    public async Task Species_external_url_must_be_http()
    {
        var response = await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("Kürbis", null, null, "javascript:alert(1)"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var duplicate = await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("Kürbis", null, null, null));
        var again = await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("kürbis", null, null, null));
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Task_without_object_needs_geometry()
    {
        var response = await client.PostAsJsonAsync("/api/tasks", NewTask("Mähen", new DateOnly(2026, 10, 8)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var free = await (await client.PostAsJsonAsync("/api/tasks", NewTask("Mähen", new DateOnly(2026, 10, 8), geometry: Square)))
            .ReadAsync<GardenTaskDto>();
        Assert.Equal("Polygon", free.Geometry!.Value.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Recurring_task_completion_creates_next_instance_and_reopen_reverts_it()
    {
        var bed = await CreateObject("Beet", "Beet", Square);
        var task = await (await client.PostAsJsonAsync("/api/tasks",
            NewTask("Gießen", new DateOnly(2026, 10, 7), bed.Id, frequency: RecurrenceFrequency.Weekly))).ReadAsync<GardenTaskDto>();
        Assert.Equal("FREQ=WEEKLY;INTERVAL=1", task.RRule);

        var result = await (await client.PostAsync($"/api/tasks/{task.Id}/complete", null)).ReadAsync<CompleteTaskResult>();
        Assert.Equal(GardenTaskStatus.Done, result.Completed.Status);
        Assert.Equal(new DateOnly(2026, 10, 14), result.Next!.DueDate);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/tasks/{task.Id}/complete", null)).StatusCode);

        await client.PostAsync($"/api/tasks/{task.Id}/reopen", null);
        var open = await (await client.GetAsync("/api/tasks")).ReadAsync<List<GardenTaskDto>>();
        Assert.Equal(task.Id, Assert.Single(open).Id);
    }

    [Fact]
    public async Task Task_outside_season_is_rejected()
    {
        var bed = await CreateObject("Beet", "Beet", Square);
        var response = await client.PostAsJsonAsync("/api/tasks",
            NewTask("Säen", new DateOnly(2026, 10, 7), bed.Id, frequency: RecurrenceFrequency.Weekly, from: 3, to: 5));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Task_list_filters_by_status_and_object()
    {
        var bed = await CreateObject("Beet", "Beet", Square);
        var other = await CreateObject("Beet 2", "Beet", Square);
        var first = await (await client.PostAsJsonAsync("/api/tasks", NewTask("A", new DateOnly(2026, 10, 7), bed.Id))).ReadAsync<GardenTaskDto>();
        await client.PostAsJsonAsync("/api/tasks", NewTask("B", new DateOnly(2026, 10, 9), other.Id));
        await client.PostAsync($"/api/tasks/{first.Id}/complete", null);

        Assert.Equal("B", Assert.Single(await (await client.GetAsync("/api/tasks")).ReadAsync<List<GardenTaskDto>>()).Title);
        Assert.Equal("A", Assert.Single(await (await client.GetAsync("/api/tasks?status=done")).ReadAsync<List<GardenTaskDto>>()).Title);
        Assert.Empty(await (await client.GetAsync($"/api/tasks?status=open&objectId={bed.Id}")).ReadAsync<List<GardenTaskDto>>());
        Assert.Equal(2, (await (await client.GetAsync("/api/tasks?status=all&from=2026-10-01&to=2026-10-31")).ReadAsync<List<GardenTaskDto>>()).Count);
    }

    [Fact]
    public async Task Task_can_be_updated_and_deleted()
    {
        var bed = await CreateObject("Beet", "Beet", Square);
        var task = await (await client.PostAsJsonAsync("/api/tasks", NewTask("A", new DateOnly(2026, 10, 7), bed.Id))).ReadAsync<GardenTaskDto>();

        var updated = await (await client.PutAsJsonAsync($"/api/tasks/{task.Id}",
            NewTask("B", new DateOnly(2026, 11, 1), geometry: Point, frequency: RecurrenceFrequency.Monthly))).ReadAsync<GardenTaskDto>();
        Assert.Equal("B", updated.Title);
        Assert.Null(updated.ObjectId);
        Assert.Equal(RecurrenceFrequency.Monthly, updated.Frequency);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
    }
}
