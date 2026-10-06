using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Api.Services;
using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Tests.Api;

public class Version2ApiTests : IAsyncLifetime
{
    private readonly BeetwerkFactory factory = new();
    private HttpClient client = null!;

    // Minimale Bytefolge mit PNG-Signatur – der Server prüft nur die Signatur, nicht den Bildinhalt.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 1, 2, 3, 4, 5, 6];
    private static readonly double[][] Corners = [[10, 54.001], [10.001, 54.001], [10.001, 54], [10, 54]];
    private static readonly JsonElement Point = JsonDocument.Parse("""{"type":"Point","coordinates":[10.0005,54.0005]}""").RootElement.Clone();

    public async Task InitializeAsync() => client = await factory.CreateLoggedInClientAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    private static ByteArrayContent File(byte[] bytes, string type)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return content;
    }

    private async Task<HttpResponseMessage> UploadOverlay(byte[] bytes, string corners)
    {
        using var form = new MultipartFormDataContent
        {
            { File(bytes, "image/png"), "file", "drohne.png" },
            { new StringContent("Drohne Juni"), "name" },
            { new StringContent(corners), "corners" },
            { new StringContent("4000"), "width" },
            { new StringContent("3000"), "height" }
        };
        return await client.PostAsync("/api/overlays", form);
    }

    private async Task<GardenObjectDto> CreatePlant(int? speciesId = null)
    {
        var types = await (await client.GetAsync("/api/object-types")).ReadAsync<List<ObjectTypeDto>>();
        var plant = types.Single(t => t.Name == "Pflanze");
        return await (await client.PostAsJsonAsync("/api/objects",
            new GardenObjectInput("Tomate 1", plant.Id, Point, null, null, speciesId, null))).ReadAsync<GardenObjectDto>();
    }

    private async Task<List<HistoryEntryDto>> History(int objectId) =>
        await (await client.GetAsync($"/api/objects/{objectId}/history")).ReadAsync<List<HistoryEntryDto>>();

    [Fact]
    public async Task Overlay_lifecycle()
    {
        var created = await (await UploadOverlay(Png, JsonSerializer.Serialize(Corners))).ReadAsync<OverlayDto>();
        Assert.Equal("Drohne Juni", created.Name);
        Assert.Equal(4000, created.Width);

        var image = await client.GetAsync(created.ImageUrl);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());

        var moved = new double[][] { [10.0001, 54.001], [10.0011, 54.001], [10.0011, 54], [10.0001, 54] };
        var updated = await (await client.PutAsJsonAsync($"/api/overlays/{created.Id}", new OverlayInput("Drohne", moved, 0.6, false)))
            .ReadAsync<OverlayDto>();
        Assert.Equal(0.6, updated.Opacity);
        Assert.False(updated.Visible);
        Assert.Equal(10.0001, updated.Corners[0][0]);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/overlays/{created.Id}")).StatusCode);
        Assert.Empty(await (await client.GetAsync("/api/overlays")).ReadAsync<List<OverlayDto>>());
    }

    [Fact]
    public async Task Overlay_rejects_non_images_and_bad_corners()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadOverlay("kein Bild"u8.ToArray(), JsonSerializer.Serialize(Corners))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadOverlay(Png, "[[1,2],[3,4]]")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadOverlay(Png, "kaputt")).StatusCode);
    }

    [Fact]
    public async Task Overlay_image_requires_login()
    {
        var created = await (await UploadOverlay(Png, JsonSerializer.Serialize(Corners))).ReadAsync<OverlayDto>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateBrowser().GetAsync(created.ImageUrl)).StatusCode);
    }

    [Fact]
    public async Task Calendar_export_is_downloadable()
    {
        var plant = await CreatePlant();
        await client.PostAsJsonAsync("/api/tasks",
            new GardenTaskInput("Gießen", null, plant.Id, null, new DateOnly(2026, 10, 9), true, 0, RecurrenceFrequency.Weekly, 1, null, null));

        var response = await client.GetAsync("/api/tasks/calendar?year=2026");
        Assert.Equal("text/calendar", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("beetwerk-2026.ics", response.Content.Headers.ContentDisposition?.ToString() ?? response.Headers.ToString());
        var ics = await response.Content.ReadAsStringAsync();
        Assert.Contains("RRULE:FREQ=WEEKLY;INTERVAL=1;UNTIL=20261231", ics);
        Assert.Contains("GEO:54.0005;10.0005", ics);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/tasks/calendar?year=1800")).StatusCode);
    }

    [Fact]
    public async Task Templates_can_be_managed_and_applied()
    {
        var species = await (await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("Tomate", null, null, null))).ReadAsync<PlantSpeciesDto>();
        var template = await (await client.PostAsJsonAsync($"/api/species/{species.Id}/templates",
                new TaskTemplateInput("Ausgeizen", null, RecurrenceFrequency.Weekly, 1, 6, 8, null, null, true, 0)))
            .ReadAsync<TaskTemplateDto>();
        var invalid = await client.PostAsJsonAsync($"/api/species/{species.Id}/templates",
            new TaskTemplateInput("Falsch", null, RecurrenceFrequency.Weekly, 1, 6, 8, 1, 1, true, 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var detail = await (await client.GetAsync($"/api/species/{species.Id}")).ReadAsync<PlantSpeciesDetailDto>();
        Assert.Equal("Ausgeizen", Assert.Single(detail.Templates).Title);

        var plant = await CreatePlant(species.Id);
        var tasks = await (await client.PostAsJsonAsync($"/api/objects/{plant.Id}/apply-templates", new ApplyTemplatesInput([template.Id])))
            .ReadAsync<List<GardenTaskDto>>();
        var task = Assert.Single(tasks);
        Assert.Equal(new DateOnly(2027, 6, 1), task.DueDate);
        Assert.Equal(plant.Id, task.ObjectId);

        var foreign = await CreatePlant();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/objects/{foreign.Id}/apply-templates", new ApplyTemplatesInput([template.Id]))).StatusCode);

        await client.PutAsJsonAsync($"/api/templates/{template.Id}",
            new TaskTemplateInput("Ausgeizen!", null, RecurrenceFrequency.None, null, null, null, null, null, false, 0));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/templates/{template.Id}")).StatusCode);
    }

    [Fact]
    public async Task History_records_creation_notes_and_task_completion()
    {
        var plant = await CreatePlant();
        Assert.Equal(ObjectLogKind.Created, Assert.Single(await History(plant.Id)).Kind);

        var note = await client.PostAsJsonAsync($"/api/objects/{plant.Id}/history", new HistoryNoteInput(new DateOnly(2026, 8, 1), "Erste Ernte: 2 kg"));
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/objects/{plant.Id}/history", new HistoryNoteInput(null, " "))).StatusCode);

        var task = await (await client.PostAsJsonAsync("/api/tasks",
                new GardenTaskInput("Düngen", null, plant.Id, null, new DateOnly(2026, 10, 7), false, 0, RecurrenceFrequency.None, 1, null, null)))
            .ReadAsync<GardenTaskDto>();
        await client.PostAsync($"/api/tasks/{task.Id}/complete", null);

        var history = await History(plant.Id);
        Assert.Contains(history, e => e.Kind == ObjectLogKind.TaskCompleted && e.Text == "Düngen" && e.CreatedBy == "admin");
        Assert.Contains(history, e => e.Kind == ObjectLogKind.Note && e.Date == new DateOnly(2026, 8, 1));

        await client.PostAsync($"/api/tasks/{task.Id}/reopen", null);
        Assert.DoesNotContain(await History(plant.Id), e => e.Kind == ObjectLogKind.TaskCompleted);

        var noteEntry = (await History(plant.Id)).Single(e => e.Kind == ObjectLogKind.Note);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/history/{noteEntry.Id}")).StatusCode);
    }

    [Fact]
    public async Task Photos_are_stored_served_and_removed()
    {
        var plant = await CreatePlant();
        using var form = new MultipartFormDataContent
        {
            { File(Jpeg, "image/jpeg"), "file", "foto.jpg" },
            { File(Jpeg, "image/jpeg"), "thumbnail", "thumb.jpg" },
            { new StringContent("Blüte"), "caption" },
            { new StringContent("2026-07-15"), "takenOn" }
        };
        var entry = await (await client.PostAsync($"/api/objects/{plant.Id}/photos", form)).ReadAsync<HistoryEntryDto>();
        Assert.Equal(ObjectLogKind.Photo, entry.Kind);
        Assert.Equal(new DateOnly(2026, 7, 15), entry.Date);
        Assert.Equal("Blüte", entry.Photo!.Caption);

        Assert.Equal(Jpeg, await (await client.GetAsync(entry.Photo.ImageUrl)).Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(entry.Photo.ThumbnailUrl)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/history/{entry.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(entry.Photo.ImageUrl)).StatusCode);
    }

    [Fact]
    public async Task Deleting_object_removes_photo_files()
    {
        var plant = await CreatePlant();
        using var form = new MultipartFormDataContent
        {
            { File(Jpeg, "image/jpeg"), "file", "foto.jpg" },
            { File(Jpeg, "image/jpeg"), "thumbnail", "thumb.jpg" }
        };
        var entry = await (await client.PostAsync($"/api/objects/{plant.Id}/photos", form)).ReadAsync<HistoryEntryDto>();
        var photosDir = Directory.GetDirectories(Path.GetTempPath() + "beetwerk-tests")
            .Select(d => Path.Combine(d, "uploads", "photos"))
            .Where(Directory.Exists)
            .SelectMany(Directory.GetFiles)
            .Count();
        Assert.True(photosDir >= 2);

        await client.DeleteAsync($"/api/objects/{plant.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(entry.Photo!.ImageUrl)).StatusCode);
    }

    [Fact]
    public async Task Photo_upload_requires_both_files()
    {
        var plant = await CreatePlant();
        using var form = new MultipartFormDataContent { { File(Jpeg, "image/jpeg"), "file", "foto.jpg" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/objects/{plant.Id}/photos", form)).StatusCode);
    }

    [Fact]
    public async Task Neighbor_distance_and_relation_list()
    {
        var garden = await (await client.GetAsync("/api/garden")).ReadAsync<GardenDto>();
        Assert.Equal(1.0, garden.NeighborWarningDistance);

        var updated = await (await client.PutAsJsonAsync("/api/garden", garden with { NeighborWarningDistance = 2.5 })).ReadAsync<GardenDto>();
        Assert.Equal(2.5, updated.NeighborWarningDistance);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/garden", garden with { NeighborWarningDistance = 0 })).StatusCode);

        var a = await (await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("Kartoffel", null, null, null))).ReadAsync<PlantSpeciesDto>();
        var b = await (await client.PostAsJsonAsync("/api/species", new PlantSpeciesInput("Tomate", null, null, null))).ReadAsync<PlantSpeciesDto>();
        await client.PostAsJsonAsync("/api/relations", new NeighborRelationInput(b.Id, a.Id, NeighborRating.Bad, null, null));

        var relation = Assert.Single(await (await client.GetAsync("/api/relations")).ReadAsync<List<RelationDto>>());
        Assert.Equal(NeighborRating.Bad, relation.Rating);
        Assert.Equal(Math.Min(a.Id, b.Id), relation.SpeciesAId);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, null)]
    [InlineData(new byte[] { }, null)]
    public void Image_type_is_detected_by_signature(byte[] header, string? expected)
    {
        Assert.Equal(expected, UploadStore.DetectImageType(header));
    }
}
