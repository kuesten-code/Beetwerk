using System.Globalization;
using System.Text;
using System.Text.Json;
using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Api.Services;

/// <summary>
/// Erzeugt einen iCalendar-Export (RFC 5545) der Aufgaben eines Jahres als ganztägige Termine.
/// Wiederkehrende Aufgaben werden als RRULE bis Jahresende abgebildet, nicht als Einzeltermine.
/// </summary>
public static class CalendarExporter
{
    public sealed record Options(int Year, bool IncludeDone, string CalendarName, int NotifyHour, DateTimeOffset Now);

    public static string Export(IEnumerable<GardenTask> tasks, Options options)
    {
        var yearStart = new DateOnly(options.Year, 1, 1);
        var yearEnd = new DateOnly(options.Year, 12, 31);
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Kuestencode//Beetwerk//DE",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "X-WR-CALNAME:" + Escape($"{options.CalendarName} {options.Year}")
        };

        foreach (var task in tasks.OrderBy(t => t.DueDate).ThenBy(t => t.Id))
        {
            if (task.Status == GardenTaskStatus.Done)
            {
                if (options.IncludeDone && task.DueDate >= yearStart && task.DueDate <= yearEnd)
                    lines.AddRange(Event(task, task.DueDate, rrule: null, done: true, options));
                continue;
            }

            if (task.Recurrence is { } rule)
            {
                // DTSTART muss eine echte Instanz der Serie sein; liegt die offene Instanz vor dem Jahr, startet der Export am ersten Termin im Jahr.
                var start = task.DueDate >= yearStart ? task.DueDate : rule.NextAfter(yearStart.AddDays(-1));
                if (start is { } first && first <= yearEnd)
                    lines.AddRange(Event(task, first, $"{rule.ToRRule()};UNTIL={yearEnd:yyyyMMdd}", done: false, options));
            }
            else if (task.DueDate >= yearStart && task.DueDate <= yearEnd)
            {
                lines.AddRange(Event(task, task.DueDate, rrule: null, done: false, options));
            }
        }

        lines.Add("END:VCALENDAR");
        var builder = new StringBuilder();
        foreach (var line in lines)
            builder.Append(Fold(line)).Append("\r\n");
        return builder.ToString();
    }

    private static IEnumerable<string> Event(GardenTask task, DateOnly start, string? rrule, bool done, Options options)
    {
        yield return "BEGIN:VEVENT";
        yield return $"UID:task-{task.Id}@beetwerk";
        yield return "DTSTAMP:" + options.Now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        yield return $"DTSTART;VALUE=DATE:{start:yyyyMMdd}";
        yield return $"DTEND;VALUE=DATE:{start.AddDays(1):yyyyMMdd}";
        if (rrule is not null)
            yield return "RRULE:" + rrule;
        yield return "SUMMARY:" + Escape(done ? "✓ " + task.Title : task.Title);

        var description = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.Description))
            description.Add(task.Description);
        if (task.Object is { } obj)
            description.Add($"Objekt: {obj.Name}");
        if (description.Count > 0)
            yield return "DESCRIPTION:" + Escape(string.Join("\n", description));

        if (PointOf(task) is { } point)
            yield return string.Create(CultureInfo.InvariantCulture, $"GEO:{point.Lat:0.######};{point.Lng:0.######}");

        yield return "TRANSP:TRANSPARENT";

        if (task.Notify && !done)
        {
            yield return "BEGIN:VALARM";
            yield return "ACTION:DISPLAY";
            yield return "DESCRIPTION:" + Escape(task.Title);
            yield return "TRIGGER:" + AlarmTrigger(task.LeadDays, options.NotifyHour);
            yield return "END:VALARM";
        }
        yield return "END:VEVENT";
    }

    /// <summary>
    /// Ganztägige Termine beginnen um Mitternacht. Die Erinnerung soll wie der Push zur Benachrichtigungsstunde kommen,
    /// <paramref name="leadDays"/> Tage vorher – relativ zum Terminbeginn also (Stunde − 24 × Tage).
    /// </summary>
    public static string AlarmTrigger(int leadDays, int notifyHour)
    {
        var hours = notifyHour - 24 * leadDays;
        return hours >= 0 ? $"PT{hours}H" : $"-PT{-hours}H";
    }

    private static (double Lat, double Lng)? PointOf(GardenTask task)
    {
        var json = task.GeometryGeoJson ?? task.Object?.GeometryGeoJson;
        if (json is null)
            return null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("type").GetString() != "Point")
            return null;
        var coordinates = root.GetProperty("coordinates");
        return (coordinates[1].GetDouble(), coordinates[0].GetDouble());
    }

    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    /// <summary>Faltet Zeilen nach RFC 5545 auf höchstens 75 Oktette, ohne UTF-8-Zeichen zu zerteilen.</summary>
    public static string Fold(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.Length <= 75)
            return line;

        var builder = new StringBuilder();
        var count = 0;
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (count + size > limit)
            {
                builder.Append("\r\n ");
                count = 0;
                limit = 74; // Folgezeilen beginnen mit einem Leerzeichen.
            }
            builder.Append(rune.ToString());
            count += size;
        }
        return builder.ToString();
    }
}
