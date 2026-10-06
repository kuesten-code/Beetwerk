using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Endpoints;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Push;

/// <summary>
/// Ein Durchlauf des Schedulers: schickt für jede Aufgabe, deren Vorlauf begonnen hat, genau eine Benachrichtigung.
/// Aufgaben gehören keinem einzelnen Nutzer, daher gehen die Nachrichten an alle registrierten Geräte.
/// </summary>
public class TaskNotifier(BeetwerkDbContext db, PushDispatcher dispatcher, TimeProvider time, BeetwerkOptions options)
{
    public const int SummaryThreshold = 5;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = time.GetLocalNow();
        if (now.Hour < options.NotifyHour)
            return 0;

        var today = DateOnly.FromDateTime(now.DateTime);
        var horizon = today.AddDays(TaskEndpoints.MaxLeadDays);
        var candidates = await db.Tasks
            .Where(t => t.Status == GardenTaskStatus.Open && t.Notify && t.NotifiedOn == null && t.DueDate <= horizon)
            .OrderBy(t => t.DueDate)
            .ToListAsync(ct);
        var due = candidates.Where(t => t.NotifyFrom <= today).ToList();
        if (due.Count == 0 || !await db.PushSubscriptions.AnyAsync(ct))
            return 0;

        List<PushMessage> messages = due.Count > SummaryThreshold
            ? [new PushMessage($"{due.Count} Gartenaufgaben stehen an", string.Join(", ", due.Take(3).Select(t => t.Title)) + " …", "/aufgaben", "summary")]
            : due.Select(t => new PushMessage(t.Title, DueText(t.DueDate, today), $"/aufgaben/{t.Id}", $"task-{t.Id}")).ToList();

        foreach (var message in messages)
            await dispatcher.SendAsync(message, userId: null, ct);

        foreach (var task in due)
            task.NotifiedOn = today;
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    public static string DueText(DateOnly due, DateOnly today) => (due.DayNumber - today.DayNumber) switch
    {
        < 0 => $"Überfällig seit {due:dd.MM.}",
        0 => "Heute fällig",
        1 => "Morgen fällig",
        var days => $"Fällig in {days} Tagen ({due:dd.MM.})"
    };
}
