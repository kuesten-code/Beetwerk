using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;
using Kuestencode.Beetwerk.Domain.Geometry;
using Kuestencode.Beetwerk.Domain.Seed;

namespace Kuestencode.Beetwerk.Tests.Domain;

public class GeoJsonGeometryTests
{
    [Theory]
    [InlineData("""{"type":"Point","coordinates":[10.1,54.3]}""", GeometryKinds.Point)]
    [InlineData("""{"type":"LineString","coordinates":[[10.1,54.3],[10.2,54.3]]}""", GeometryKinds.LineString)]
    [InlineData("""{"type":"Polygon","coordinates":[[[10,54],[10.1,54],[10.1,54.1],[10,54]]]}""", GeometryKinds.Polygon)]
    public void Accepts_supported_geometries(string json, GeometryKinds kind)
    {
        var result = GeoJsonGeometry.Parse(json);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(kind, result.Kind);
    }

    [Theory]
    [InlineData("""{"type":"MultiPoint","coordinates":[[10,54]]}""")]
    [InlineData("""{"type":"Point","coordinates":[200,54]}""")]
    [InlineData("""{"type":"Point","coordinates":["a","b"]}""")]
    [InlineData("""{"type":"Point"}""")]
    [InlineData("""{"coordinates":[10,54]}""")]
    [InlineData("""{"type":"LineString","coordinates":[[10,54]]}""")]
    [InlineData("""{"type":"Polygon","coordinates":[[[10,54],[10.1,54],[10.1,54.1],[10.2,54]]]}""")]
    [InlineData("""{"type":"Polygon","coordinates":[]}""")]
    [InlineData("""[1,2]""")]
    public void Rejects_invalid_geometries(string json)
    {
        var result = GeoJsonGeometry.Parse(json);
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Normalizes_by_dropping_foreign_members()
    {
        var result = GeoJsonGeometry.Parse("""{"type":"Point","coordinates":[10,54],"bbox":[1,2,3,4],"evil":"x"}""");
        Assert.Equal("""{"type":"Point","coordinates":[10,54]}""", result.NormalizedJson);
    }

    [Fact]
    public void Missing_geometry_is_invalid()
    {
        Assert.False(GeoJsonGeometry.Parse((System.Text.Json.JsonElement?)null).IsValid);
    }
}

public class NeighborRelationTests
{
    [Fact]
    public void Pair_is_stored_in_ascending_order()
    {
        var relation = new NeighborRelation();
        relation.SetPair(7, 3);
        Assert.Equal(3, relation.SpeciesAId);
        Assert.Equal(7, relation.SpeciesBId);
        Assert.Equal(7, relation.OtherThan(3));
        Assert.Equal(3, relation.OtherThan(7));
    }

    [Fact]
    public void Relation_to_itself_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new NeighborRelation().SetPair(4, 4));
    }
}

public class GardenTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Completing_one_time_task_creates_no_successor()
    {
        var task = new GardenTask { Title = "Rasen mähen", DueDate = new DateOnly(2026, 10, 6) };
        Assert.Null(task.Complete(Now, new DateOnly(2026, 10, 6)));
        Assert.Equal(GardenTaskStatus.Done, task.Status);
        Assert.Equal(Now, task.CompletedAt);
    }

    [Fact]
    public void Completing_recurring_task_creates_next_instance_of_series()
    {
        var task = new GardenTask
        {
            Id = 12,
            Title = "Gießen",
            DueDate = new DateOnly(2026, 10, 6),
            SeriesStart = new DateOnly(2026, 10, 6),
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 1,
            Notify = true,
            LeadDays = 1,
            NotifiedOn = new DateOnly(2026, 10, 5)
        };

        var next = task.Complete(Now, new DateOnly(2026, 10, 6))!;

        Assert.Equal(new DateOnly(2026, 10, 13), next.DueDate);
        Assert.Equal(task.SeriesId, next.SeriesId);
        Assert.Equal(12, next.GeneratedFromTaskId);
        Assert.Equal(GardenTaskStatus.Open, next.Status);
        Assert.Null(next.NotifiedOn);
        Assert.True(next.Notify);
    }

    [Fact]
    public void Completing_twice_does_nothing()
    {
        var task = new GardenTask { Title = "x", DueDate = new DateOnly(2026, 1, 1), Frequency = RecurrenceFrequency.Daily, SeriesStart = new DateOnly(2026, 1, 1) };
        Assert.NotNull(task.Complete(Now, new DateOnly(2026, 1, 1)));
        Assert.Null(task.Complete(Now, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Reopen_resets_status()
    {
        var task = new GardenTask { Title = "x", DueDate = new DateOnly(2026, 1, 1) };
        task.Complete(Now, new DateOnly(2026, 1, 1));
        task.Reopen();
        Assert.Equal(GardenTaskStatus.Open, task.Status);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void NotifyFrom_subtracts_lead_days()
    {
        Assert.Equal(new DateOnly(2026, 9, 29), new GardenTask { Title = "x", DueDate = new DateOnly(2026, 10, 6), LeadDays = 7 }.NotifyFrom);
    }
}

public class DefaultObjectTypesTests
{
    [Fact]
    public void Contains_types_from_specification()
    {
        var types = DefaultObjectTypes.Create();
        Assert.Equal(["Pflanze", "Beet", "Hecke", "Gewächshaus", "Stall", "Gerät", "Sonstiges"], types.Select(t => t.Name));
        Assert.True(types.Single(t => t.Name == "Hecke").Allows(GeometryKinds.LineString));
        Assert.False(types.Single(t => t.Name == "Beet").Allows(GeometryKinds.Point));
        Assert.Contains(types.Single(t => t.Name == "Pflanze").Fields, f => f.Type == ObjectFieldType.Species);
        Assert.Empty(types.Single(t => t.Name == "Stall").Fields);
    }
}
