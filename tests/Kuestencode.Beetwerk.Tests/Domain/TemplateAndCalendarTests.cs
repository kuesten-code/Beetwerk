using Kuestencode.Beetwerk.Api.Services;
using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;
using Kuestencode.Beetwerk.Domain.Templates;

namespace Kuestencode.Beetwerk.Tests.Domain;

public class SpeciesTaskTemplateTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void Fixed_start_date_uses_next_occurrence()
    {
        var template = new SpeciesTaskTemplate { Title = "Rosen schneiden", Frequency = RecurrenceFrequency.Yearly, StartMonth = 3, StartDay = 15 };
        Assert.Equal(new DateOnly(2027, 3, 15), template.FirstDueDate(Today));
        Assert.Equal(new DateOnly(2026, 10, 7), new SpeciesTaskTemplate { Title = "x", StartMonth = 10, StartDay = 7 }.FirstDueDate(Today));
    }

    [Fact]
    public void Start_day_is_clamped_to_month_length()
    {
        Assert.Equal(new DateOnly(2027, 2, 28), new SpeciesTaskTemplate { Title = "x", StartMonth = 2, StartDay = 31 }.FirstDueDate(Today));
    }

    [Fact]
    public void Seasonal_template_starts_at_next_season_begin()
    {
        var template = new SpeciesTaskTemplate { Title = "Ausgeizen", Frequency = RecurrenceFrequency.Weekly, SeasonStartMonth = 6, SeasonEndMonth = 8 };
        Assert.Equal(new DateOnly(2027, 6, 1), template.FirstDueDate(Today));
        Assert.Equal(new DateOnly(2026, 7, 2), template.FirstDueDate(new DateOnly(2026, 7, 2)));
        Assert.Equal(new DateOnly(2026, 6, 1), template.FirstDueDate(new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void Without_start_and_season_the_task_is_due_today()
    {
        Assert.Equal(Today, new SpeciesTaskTemplate { Title = "Angießen" }.FirstDueDate(Today));
    }

    [Fact]
    public void Validate_checks_season_and_values()
    {
        Assert.Null(new SpeciesTaskTemplate { Title = "ok", Frequency = RecurrenceFrequency.Weekly, SeasonStartMonth = 6, SeasonEndMonth = 8 }.Validate());
        Assert.NotNull(new SpeciesTaskTemplate { Title = " " }.Validate());
        Assert.NotNull(new SpeciesTaskTemplate { Title = "x", StartMonth = 13 }.Validate());
        Assert.NotNull(new SpeciesTaskTemplate { Title = "x", LeadDays = -1 }.Validate());
        // Fester Start im Januar liegt außerhalb der Saison Juni–August.
        Assert.NotNull(new SpeciesTaskTemplate { Title = "x", Frequency = RecurrenceFrequency.Weekly, StartMonth = 1, SeasonStartMonth = 6, SeasonEndMonth = 8 }.Validate());
    }

    [Fact]
    public void CreateTask_copies_template_and_anchors_series()
    {
        var template = new SpeciesTaskTemplate
        {
            Title = "Ausgeizen", Description = "Seitentriebe entfernen", Frequency = RecurrenceFrequency.Weekly,
            SeasonStartMonth = 6, SeasonEndMonth = 8, Notify = true, LeadDays = 1
        };
        var task = template.CreateTask(42, new DateOnly(2026, 7, 2));
        Assert.Equal(42, task.ObjectId);
        Assert.Equal(new DateOnly(2026, 7, 2), task.DueDate);
        Assert.Equal(task.DueDate, task.SeriesStart);
        Assert.Equal(6, task.SeasonStartMonth);
        Assert.True(task.Notify);
    }
}

public class CalendarExporterTests
{
    private static readonly CalendarExporter.Options Options =
        new(2026, false, "Mein Garten", 8, new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));

    private static GardenTask Task(int id, string title, DateOnly due, RecurrenceFrequency frequency = RecurrenceFrequency.None) =>
        new() { Id = id, Title = title, DueDate = due, SeriesStart = due, Frequency = frequency };

    [Fact]
    public void Exports_one_time_tasks_of_the_year_as_all_day_events()
    {
        var ics = CalendarExporter.Export(
            [Task(1, "Rasen mähen", new DateOnly(2026, 5, 3)), Task(2, "Nächstes Jahr", new DateOnly(2027, 1, 2))], Options);

        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20260503\r\n", ics);
        Assert.Contains("DTEND;VALUE=DATE:20260504\r\n", ics);
        Assert.Contains("UID:task-1@beetwerk", ics);
        Assert.DoesNotContain("Nächstes Jahr", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }

    [Fact]
    public void Recurring_task_becomes_rrule_limited_to_year()
    {
        var task = Task(3, "Gießen", new DateOnly(2026, 6, 1), RecurrenceFrequency.Weekly);
        task.SeasonStartMonth = 5;
        task.SeasonEndMonth = 9;

        var ics = CalendarExporter.Export([task], Options);

        Assert.Contains("RRULE:FREQ=WEEKLY;INTERVAL=1;BYMONTH=5,6,7,8,9;UNTIL=20261231\r\n", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20260601", ics);
    }

    [Fact]
    public void Recurring_task_from_previous_year_starts_at_first_occurrence_in_year()
    {
        var ics = CalendarExporter.Export([Task(4, "Füttern", new DateOnly(2025, 12, 29), RecurrenceFrequency.Weekly)], Options);
        Assert.Contains("DTSTART;VALUE=DATE:20260105", ics);
    }

    [Fact]
    public void Done_tasks_are_optional()
    {
        var done = Task(5, "Erledigt", new DateOnly(2026, 4, 1));
        done.Status = GardenTaskStatus.Done;

        Assert.DoesNotContain("Erledigt", CalendarExporter.Export([done], Options));
        Assert.Contains("SUMMARY:✓ Erledigt", CalendarExporter.Export([done], Options with { IncludeDone = true }));
    }

    [Fact]
    public void Alarm_geo_description_and_escaping()
    {
        var task = Task(6, "Hecke schneiden, oben; vorsichtig", new DateOnly(2026, 6, 10));
        task.Notify = true;
        task.LeadDays = 2;
        task.Description = "Zeile 1\nZeile 2";
        task.GeometryGeoJson = """{"type":"Point","coordinates":[8.9099,54.4718]}""";

        var ics = CalendarExporter.Export([task], Options);

        Assert.Contains("SUMMARY:Hecke schneiden\\, oben\\; vorsichtig", ics);
        Assert.Contains("DESCRIPTION:Zeile 1\\nZeile 2", ics);
        Assert.Contains("GEO:54.4718;8.9099", ics);
        Assert.Contains("BEGIN:VALARM", ics);
        Assert.Contains("TRIGGER:-PT40H", ics);
    }

    [Fact]
    public void Object_name_is_referenced()
    {
        var task = Task(7, "Gießen", new DateOnly(2026, 6, 10));
        task.Object = new GardenObject { Name = "Hochbeet", GeometryGeoJson = """{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}""" };

        var ics = CalendarExporter.Export([task], Options);

        Assert.Contains("DESCRIPTION:Objekt: Hochbeet", ics);
        Assert.DoesNotContain("GEO:", ics);
    }

    [Theory]
    [InlineData(0, 8, "PT8H")]
    [InlineData(1, 8, "-PT16H")]
    [InlineData(2, 0, "-PT48H")]
    public void Alarm_trigger_fires_at_notify_hour(int leadDays, int hour, string expected)
    {
        Assert.Equal(expected, CalendarExporter.AlarmTrigger(leadDays, hour));
    }

    [Fact]
    public void Long_lines_are_folded_without_breaking_characters()
    {
        var line = "SUMMARY:" + string.Concat(Enumerable.Repeat("Gießkannenübung ", 10));
        var folded = CalendarExporter.Fold(line);
        var parts = folded.Split("\r\n");

        Assert.True(parts.Length > 1);
        Assert.All(parts, p => Assert.True(System.Text.Encoding.UTF8.GetByteCount(p) <= 75));
        Assert.All(parts.Skip(1), p => Assert.StartsWith(" ", p));
        Assert.Equal(line, string.Concat(parts.Select((p, i) => i == 0 ? p : p[1..])));
    }
}
