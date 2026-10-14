namespace Wellbeing.Service.Tracking;

public sealed class TrackingWorker(Tracker tracker, ILogger<TrackingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            tracker.Initialize();
        }
        catch (Exception e)
        {
            // Без учёта служба бесполезна. Ненулевой код выхода — чтобы сработали recovery actions службы.
            log.LogCritical(e, "Не удалось запустить учёт");
            Environment.Exit(1);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    tracker.Tick();
                }
                catch (Exception e)
                {
                    log.LogError(e, "Ошибка в цикле учёта");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        tracker.Flush();
    }
}
