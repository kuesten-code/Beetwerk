using System.Globalization;
using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Domain.Recurrence;

/// <summary>
/// Wiederholungsregel, bewusst auf den Umfang von iCalendar-RRULEs begrenzt (FREQ, INTERVAL, BYMONTH),
/// damit der spätere .ics-Export sie verlustfrei abbilden kann. Instanzen werden wie bei RRULE
/// vom Serienanker aus berechnet; Termine, die es im Monat nicht gibt (31.02.), entfallen.
/// </summary>
public sealed record RecurrenceRule(
    RecurrenceFrequency Frequency,
    int Interval,
    DateOnly SeriesStart,
    int? SeasonStartMonth,
    int? SeasonEndMonth)
{
    private const int MaxIterations = 5000;

    public bool HasSeason => SeasonStartMonth.HasValue && SeasonEndMonth.HasValue;

    /// <summary>Liefert eine Fehlermeldung oder null, wenn die Regel gültig ist.</summary>
    public string? Validate()
    {
        if (Frequency == RecurrenceFrequency.None)
            return "Eine Wiederholungsregel braucht eine Frequenz.";
        if (Interval is < 1 or > 365)
            return "Das Intervall muss zwischen 1 und 365 liegen.";
        if (SeasonStartMonth.HasValue != SeasonEndMonth.HasValue)
            return "Für das Saisonfenster müssen Start- und Endmonat angegeben werden.";
        if (HasSeason && (SeasonStartMonth is < 1 or > 12 || SeasonEndMonth is < 1 or > 12))
            return "Monate des Saisonfensters müssen zwischen 1 und 12 liegen.";
        // RRULE mit FREQ=YEARLY;BYMONTH erweitert statt zu filtern – das wäre eine andere Semantik.
        if (HasSeason && Frequency == RecurrenceFrequency.Yearly)
            return "Bei jährlicher Wiederholung ist kein Saisonfenster möglich.";
        if (!IsInSeason(SeriesStart))
            return "Das Fälligkeitsdatum liegt außerhalb des Saisonfensters.";
        if (NextAfter(SeriesStart) is null)
            return "Mit diesem Saisonfenster gibt es keine weiteren Termine.";
        return null;
    }

    public bool IsInSeason(DateOnly date)
    {
        if (!HasSeason)
            return true;

        var start = SeasonStartMonth!.Value;
        var end = SeasonEndMonth!.Value;
        return start <= end
            ? date.Month >= start && date.Month <= end
            : date.Month >= start || date.Month <= end;
    }

    /// <summary>Erste Instanz der Serie, die echt nach <paramref name="after"/> liegt.</summary>
    public DateOnly? NextAfter(DateOnly after)
    {
        var k = Math.Max(0, EstimateIndex(after));
        for (var i = 0; i < MaxIterations; i++, k++)
        {
            var candidate = Occurrence(k);
            if (candidate is { } date && date > after && IsInSeason(date))
                return date;
        }
        return null;
    }

    /// <summary>Nächste Instanz nach dem aktuellen Termin, aber nicht vor <paramref name="notBefore"/>,
    /// damit eine verspätet erledigte Aufgabe keine bereits verstrichenen Instanzen erzeugt.</summary>
    public DateOnly? NextInstance(DateOnly currentDue, DateOnly notBefore)
    {
        var after = currentDue >= notBefore ? currentDue : notBefore.AddDays(-1);
        return NextAfter(after);
    }

    public string ToRRule()
    {
        var parts = new List<string>
        {
            "FREQ=" + Frequency.ToString().ToUpperInvariant(),
            "INTERVAL=" + Interval.ToString(CultureInfo.InvariantCulture)
        };
        if (HasSeason)
            parts.Add("BYMONTH=" + string.Join(',', SeasonMonths()));
        return string.Join(';', parts);
    }

    private IEnumerable<int> SeasonMonths() =>
        Enumerable.Range(1, 12).Where(m => IsInSeason(new DateOnly(2000, m, 1)));

    private DateOnly? Occurrence(int k)
    {
        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                return SeriesStart.AddDays(k * Interval);
            case RecurrenceFrequency.Weekly:
                return SeriesStart.AddDays(k * Interval * 7);
            case RecurrenceFrequency.Monthly:
            {
                var months = SeriesStart.Year * 12 + SeriesStart.Month - 1 + k * Interval;
                return TryCreate(months / 12, months % 12 + 1, SeriesStart.Day);
            }
            case RecurrenceFrequency.Yearly:
                return TryCreate(SeriesStart.Year + k * Interval, SeriesStart.Month, SeriesStart.Day);
            default:
                throw new InvalidOperationException("Keine Wiederholung definiert.");
        }
    }

    private int EstimateIndex(DateOnly after)
    {
        var days = after.DayNumber - SeriesStart.DayNumber;
        return Frequency switch
        {
            RecurrenceFrequency.Daily => days / Interval,
            RecurrenceFrequency.Weekly => days / (7 * Interval),
            RecurrenceFrequency.Monthly =>
                ((after.Year - SeriesStart.Year) * 12 + after.Month - SeriesStart.Month) / Interval - 1,
            RecurrenceFrequency.Yearly => (after.Year - SeriesStart.Year) / Interval - 1,
            _ => 0
        };
    }

    private static DateOnly? TryCreate(int year, int month, int day) =>
        year is >= 1 and <= 9999 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;
}
