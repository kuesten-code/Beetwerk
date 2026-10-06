namespace Kuestencode.Beetwerk.Api.Push;

public class TaskNotificationService(IServiceScopeFactory scopes, ILogger<TaskNotificationService> logger, TimeProvider time)
    : BackgroundService
{
    public static readonly TimeSpan Period = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<TaskNotifier>().RunAsync(stoppingToken);
                if (sent > 0)
                    logger.LogInformation("{Count} Aufgabe(n) per Push gemeldet.", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Prüfung fälliger Aufgaben fehlgeschlagen.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
