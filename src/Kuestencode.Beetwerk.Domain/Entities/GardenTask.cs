using Kuestencode.Beetwerk.Domain.Enums;
using Kuestencode.Beetwerk.Domain.Recurrence;

namespace Kuestencode.Beetwerk.Domain.Entities;

public class GardenTask
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }

    public int? ObjectId { get; set; }
    public GardenObject? Object { get; set; }
    /// <summary>Eigene Geometrie, nur gesetzt wenn die Aufgabe an keinem Objekt hängt.</summary>
    public string? GeometryGeoJson { get; set; }

    public DateOnly DueDate { get; set; }
    public GardenTaskStatus Status { get; set; } = GardenTaskStatus.Open;
    public DateTimeOffset? CompletedAt { get; set; }

    public bool Notify { get; set; }
    public int LeadDays { get; set; }
    /// <summary>Datum der letzten Push-Benachrichtigung; verhindert Mehrfachversand innerhalb einer Fälligkeit.</summary>
    public DateOnly? NotifiedOn { get; set; }

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.None;
    public int Interval { get; set; } = 1;
    public int? SeasonStartMonth { get; set; }
    public int? SeasonEndMonth { get; set; }
    /// <summary>Anker der Serie (entspricht DTSTART), alle Instanzen liegen auf diesem Raster.</summary>
    public DateOnly SeriesStart { get; set; }
    public Guid SeriesId { get; set; } = Guid.NewGuid();
    /// <summary>Instanz, deren Erledigung diese Instanz erzeugt hat; erlaubt das Zurücknehmen einer Erledigung.</summary>
    public int? GeneratedFromTaskId { get; set; }

    public bool IsRecurring => Frequency != RecurrenceFrequency.None;

    public RecurrenceRule? Recurrence => IsRecurring
        ? new RecurrenceRule(Frequency, Interval, SeriesStart, SeasonStartMonth, SeasonEndMonth)
        : null;

    public DateOnly NotifyFrom => DueDate.AddDays(-LeadDays);

    /// <summary>Markiert die Aufgabe als erledigt und liefert bei Wiederholung die nächste Instanz der Serie.</summary>
    public GardenTask? Complete(DateTimeOffset now, DateOnly today)
    {
        if (Status == GardenTaskStatus.Done)
            return null;

        Status = GardenTaskStatus.Done;
        CompletedAt = now;

        if (Recurrence?.NextInstance(DueDate, today) is not { } next)
            return null;

        return new GardenTask
        {
            Title = Title,
            Description = Description,
            ObjectId = ObjectId,
            GeometryGeoJson = GeometryGeoJson,
            DueDate = next,
            Notify = Notify,
            LeadDays = LeadDays,
            Frequency = Frequency,
            Interval = Interval,
            SeasonStartMonth = SeasonStartMonth,
            SeasonEndMonth = SeasonEndMonth,
            SeriesStart = SeriesStart,
            SeriesId = SeriesId,
            GeneratedFromTaskId = Id
        };
    }

    public void Reopen()
    {
        Status = GardenTaskStatus.Open;
        CompletedAt = null;
    }
}
