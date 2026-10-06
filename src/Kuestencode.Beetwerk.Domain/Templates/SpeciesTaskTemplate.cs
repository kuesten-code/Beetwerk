using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Enums;
using Kuestencode.Beetwerk.Domain.Recurrence;

namespace Kuestencode.Beetwerk.Domain.Templates;

/// <summary>Typische Aufgabe einer Pflanzenart, aus der für ein konkretes Objekt eine Aufgabe erzeugt wird.</summary>
public class SpeciesTaskTemplate
{
    public int Id { get; set; }
    public int PlantSpeciesId { get; set; }
    public PlantSpecies? PlantSpecies { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.None;
    public int Interval { get; set; } = 1;
    public int? SeasonStartMonth { get; set; }
    public int? SeasonEndMonth { get; set; }
    /// <summary>Fester Termin im Jahr (z. B. Rosenschnitt im März); ohne Angabe ab heute bzw. ab Saisonbeginn.</summary>
    public int? StartMonth { get; set; }
    public int StartDay { get; set; } = 1;
    public bool Notify { get; set; } = true;
    public int LeadDays { get; set; }

    /// <summary>Erster Fälligkeitstermin, wenn die Vorlage am Tag <paramref name="today"/> angewendet wird.</summary>
    public DateOnly FirstDueDate(DateOnly today)
    {
        if (StartMonth is { } month)
        {
            var candidate = DateInYear(today.Year, month, StartDay);
            return candidate >= today ? candidate : DateInYear(today.Year + 1, month, StartDay);
        }

        if (Frequency != RecurrenceFrequency.None && SeasonStartMonth is { } seasonStart)
        {
            var rule = new RecurrenceRule(Frequency, Interval, today, SeasonStartMonth, SeasonEndMonth);
            if (rule.IsInSeason(today))
                return today;
            var begin = new DateOnly(today.Year, seasonStart, 1);
            return begin > today ? begin : begin.AddYears(1);
        }

        return today;
    }

    /// <summary>Liefert eine Fehlermeldung oder null, wenn die Vorlage gültig ist.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            return "Die Vorlage braucht einen Titel.";
        if (StartMonth is < 1 or > 12)
            return "Der Startmonat muss zwischen 1 und 12 liegen.";
        if (StartDay is < 1 or > 31)
            return "Der Starttag muss zwischen 1 und 31 liegen.";
        if (LeadDays is < 0 or > 365)
            return "Der Vorlauf muss zwischen 0 und 365 Tagen liegen.";
        if (Frequency == RecurrenceFrequency.None)
            return null;

        // Prüfung an einem festen Stichtag: Der erste Termin muss eine gültige Wiederholungsregel ergeben.
        var start = FirstDueDate(new DateOnly(2026, 1, 1));
        return new RecurrenceRule(Frequency, Interval, start, SeasonStartMonth, SeasonEndMonth).Validate();
    }

    public GardenTask CreateTask(int objectId, DateOnly today)
    {
        var due = FirstDueDate(today);
        var recurring = Frequency != RecurrenceFrequency.None;
        return new GardenTask
        {
            Title = Title,
            Description = Description,
            ObjectId = objectId,
            DueDate = due,
            SeriesStart = due,
            Notify = Notify,
            LeadDays = LeadDays,
            Frequency = Frequency,
            Interval = recurring ? Interval : 1,
            SeasonStartMonth = recurring ? SeasonStartMonth : null,
            SeasonEndMonth = recurring ? SeasonEndMonth : null
        };
    }

    private static DateOnly DateInYear(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
