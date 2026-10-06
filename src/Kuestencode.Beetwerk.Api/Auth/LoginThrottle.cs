using System.Collections.Concurrent;

namespace Kuestencode.Beetwerk.Api.Auth;

/// <summary>
/// Sperrt einen Benutzernamen nach zu vielen Fehlversuchen für eine Weile. Ergänzt das IP-basierte
/// Rate-Limiting, das hinter einem Proxy mit gefälschten X-Forwarded-For-Headern umgangen werden könnte.
/// </summary>
public class LoginThrottle(TimeProvider time)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> failures = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLocked(string username)
    {
        if (!failures.TryGetValue(username, out var list))
            return false;
        lock (list)
        {
            Prune(list);
            return list.Count >= MaxFailures;
        }
    }

    public void RecordFailure(string username)
    {
        var list = failures.GetOrAdd(username, _ => []);
        lock (list)
        {
            Prune(list);
            list.Add(time.GetUtcNow());
        }
    }

    public void Reset(string username) => failures.TryRemove(username, out _);

    private void Prune(List<DateTimeOffset> list)
    {
        var threshold = time.GetUtcNow() - Window;
        list.RemoveAll(t => t < threshold);
    }
}
