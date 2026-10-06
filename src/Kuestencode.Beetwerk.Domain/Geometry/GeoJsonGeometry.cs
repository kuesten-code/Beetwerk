using System.Text.Json;
using System.Text.Json.Nodes;
using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Domain.Geometry;

/// <summary>Prüft und normalisiert GeoJSON-Geometrien (Point, LineString, Polygon, WGS84).</summary>
public static class GeoJsonGeometry
{
    public sealed record Result(bool IsValid, GeometryKinds Kind, string? NormalizedJson, string? Error)
    {
        public static Result Fail(string error) => new(false, GeometryKinds.None, null, error);
    }

    public static Result Parse(JsonElement? geometry)
    {
        if (geometry is not { ValueKind: JsonValueKind.Object } element)
            return Result.Fail("Geometrie fehlt.");

        if (!element.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            return Result.Fail("Geometrie ohne Typ.");
        if (!element.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
            return Result.Fail("Geometrie ohne Koordinaten.");

        var type = typeElement.GetString();
        var (kind, error) = type switch
        {
            "Point" => (GeometryKinds.Point, ValidatePosition(coordinates)),
            "LineString" => (GeometryKinds.LineString, ValidateLine(coordinates)),
            "Polygon" => (GeometryKinds.Polygon, ValidatePolygon(coordinates)),
            _ => (GeometryKinds.None, $"Geometrietyp '{type}' wird nicht unterstützt.")
        };
        if (error is not null)
            return Result.Fail(error);

        var normalized = new JsonObject
        {
            ["type"] = type,
            ["coordinates"] = JsonNode.Parse(coordinates.GetRawText())
        };
        return new Result(true, kind, normalized.ToJsonString(), null);
    }

    public static Result Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement.Clone());
    }

    private static string? ValidatePosition(JsonElement position)
    {
        if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() < 2)
            return "Ungültige Position.";
        var lng = position[0];
        var lat = position[1];
        if (lng.ValueKind != JsonValueKind.Number || lat.ValueKind != JsonValueKind.Number)
            return "Ungültige Position.";
        if (lng.GetDouble() is < -180 or > 180 || lat.GetDouble() is < -90 or > 90)
            return "Koordinaten außerhalb von WGS84.";
        return null;
    }

    private static string? ValidateLine(JsonElement line, int minPositions = 2)
    {
        if (line.ValueKind != JsonValueKind.Array || line.GetArrayLength() < minPositions)
            return $"Eine Linie braucht mindestens {minPositions} Punkte.";
        return line.EnumerateArray().Select(ValidatePosition).FirstOrDefault(e => e is not null);
    }

    private static string? ValidatePolygon(JsonElement rings)
    {
        if (rings.GetArrayLength() < 1)
            return "Eine Fläche braucht mindestens einen Ring.";
        foreach (var ring in rings.EnumerateArray())
        {
            if (ValidateLine(ring, 4) is { } error)
                return error;
            var first = ring[0];
            var last = ring[ring.GetArrayLength() - 1];
            if (first[0].GetDouble() != last[0].GetDouble() || first[1].GetDouble() != last[1].GetDouble())
                return "Der Ring einer Fläche muss geschlossen sein.";
        }
        return null;
    }
}
