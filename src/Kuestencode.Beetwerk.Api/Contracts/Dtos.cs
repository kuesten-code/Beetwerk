using System.Text.Json;
using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Api.Contracts;

public record GardenDto(string Name, double CenterLatitude, double CenterLongitude, double Zoom, JsonElement? Boundary, double? NeighborWarningDistance = null);

public record ObjectTypeFieldDto(string Key, string Label, ObjectFieldType Type);

public record ObjectTypeDto(
    int Id, string Name, string Icon, string Color, IReadOnlyList<string> AllowedGeometries, IReadOnlyList<ObjectTypeFieldDto> Fields);

public record ObjectTypeInput(
    string Name, string? Icon, string? Color, IReadOnlyList<string>? AllowedGeometries, IReadOnlyList<ObjectTypeFieldDto>? Fields);

public record GardenObjectDto(
    int Id, string Name, int ObjectTypeId, JsonElement Geometry, string GeometryKind, string? Notes,
    int? ParentObjectId, int? PlantSpeciesId, string? PlantSpeciesName, IReadOnlyDictionary<string, string> Attributes);

public record GardenObjectInput(
    string Name, int ObjectTypeId, JsonElement? Geometry, string? Notes,
    int? ParentObjectId, int? PlantSpeciesId, Dictionary<string, string>? Attributes);

public record PlantSpeciesDto(int Id, string Name, string? ScientificName, string? Notes, string? ExternalUrl);

public record PlantSpeciesInput(string Name, string? ScientificName, string? Notes, string? ExternalUrl);

public record NeighborDto(int RelationId, int SpeciesId, string SpeciesName, NeighborRating Rating, string? Note, string? Source);

public record PlantSpeciesDetailDto(PlantSpeciesDto Species, IReadOnlyList<NeighborDto> Neighbors, int ObjectCount, IReadOnlyList<TaskTemplateDto> Templates);

public record RelationDto(int Id, int SpeciesAId, int SpeciesBId, NeighborRating Rating);

public record TaskTemplateDto(
    int Id, string Title, string? Description, RecurrenceFrequency Frequency, int Interval, int? SeasonStartMonth, int? SeasonEndMonth,
    int? StartMonth, int StartDay, bool Notify, int LeadDays);

public record TaskTemplateInput(
    string Title, string? Description, RecurrenceFrequency Frequency, int? Interval, int? SeasonStartMonth, int? SeasonEndMonth,
    int? StartMonth, int? StartDay, bool Notify, int LeadDays);

public record ApplyTemplatesInput(IReadOnlyList<int> TemplateIds);

public record NeighborRelationInput(int SpeciesId, int OtherSpeciesId, NeighborRating Rating, string? Note, string? Source);

public record GardenTaskDto(
    int Id, string Title, string? Description, int? ObjectId, string? ObjectName, JsonElement? Geometry,
    DateOnly DueDate, GardenTaskStatus Status, DateTimeOffset? CompletedAt, bool Notify, int LeadDays,
    RecurrenceFrequency Frequency, int Interval, int? SeasonStartMonth, int? SeasonEndMonth, string? RRule);

public record GardenTaskInput(
    string Title, string? Description, int? ObjectId, JsonElement? Geometry, DateOnly DueDate,
    bool Notify, int LeadDays, RecurrenceFrequency Frequency, int? Interval, int? SeasonStartMonth, int? SeasonEndMonth);

public record CompleteTaskResult(GardenTaskDto Completed, GardenTaskDto? Next);

public record PhotoDto(int Id, string ImageUrl, string ThumbnailUrl, string? Caption);

public record HistoryEntryDto(int Id, DateOnly Date, ObjectLogKind Kind, string Text, string? CreatedBy, int? TaskId, PhotoDto? Photo);

public record HistoryNoteInput(DateOnly? Date, string Text);

public record OverlayDto(int Id, string Name, int Width, int Height, double[][] Corners, double Opacity, bool Visible, string ImageUrl);

public record OverlayInput(string Name, double[][] Corners, double Opacity, bool Visible);

public record UserDto(string Username, bool IsCurrent);

public record CreateUserInput(string Username, string Password);

public record PasswordInput(string Password);

public record PushSubscriptionKeys(string P256dh, string Auth);

public record PushSubscriptionInput(string Endpoint, PushSubscriptionKeys Keys, string? DeviceLabel);

public static class Mapping
{
    public static JsonElement ToJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static IReadOnlyList<string> ToNames(GeometryKinds kinds) =>
        Enum.GetValues<GeometryKinds>()
            .Where(k => k is GeometryKinds.Point or GeometryKinds.LineString or GeometryKinds.Polygon && kinds.HasFlag(k))
            .Select(k => k.ToString())
            .ToList();

    public static GardenDto ToDto(this Garden garden) =>
        new(garden.Name, garden.CenterLatitude, garden.CenterLongitude, garden.Zoom,
            garden.BoundaryGeoJson is null ? null : ToJsonElement(garden.BoundaryGeoJson), garden.NeighborWarningDistance);

    public static ObjectTypeDto ToDto(this ObjectType type) =>
        new(type.Id, type.Name, type.Icon, type.Color, ToNames(type.AllowedGeometries),
            type.Fields.Select(f => new ObjectTypeFieldDto(f.Key, f.Label, f.Type)).ToList());

    public static GardenObjectDto ToDto(this GardenObject obj) =>
        new(obj.Id, obj.Name, obj.ObjectTypeId, ToJsonElement(obj.GeometryGeoJson), obj.GeometryKind.ToString(), obj.Notes,
            obj.ParentObjectId, obj.PlantSpeciesId, obj.PlantSpecies?.Name, obj.Attributes);

    public static PlantSpeciesDto ToDto(this PlantSpecies species) =>
        new(species.Id, species.Name, species.ScientificName, species.Notes, species.ExternalUrl);

    public static GardenTaskDto ToDto(this GardenTask task) =>
        new(task.Id, task.Title, task.Description, task.ObjectId, task.Object?.Name,
            task.GeometryGeoJson is null ? null : ToJsonElement(task.GeometryGeoJson),
            task.DueDate, task.Status, task.CompletedAt, task.Notify, task.LeadDays,
            task.Frequency, task.Interval, task.SeasonStartMonth, task.SeasonEndMonth, task.Recurrence?.ToRRule());
}

public static class ApiResults
{
    public static IResult Error(string message, int statusCode = StatusCodes.Status400BadRequest) =>
        Results.Json(new { error = message }, statusCode: statusCode);

    public static IResult Conflict(string message) => Error(message, StatusCodes.Status409Conflict);
}
