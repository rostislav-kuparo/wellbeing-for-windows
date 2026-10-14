using Wellbeing.Core.Tracking;

namespace Wellbeing.Core.Agent;

public enum AgentAction
{
    None,

    /// <summary>Агента нет — запустить.</summary>
    Launch,

    /// <summary>Процесс агента есть, но heartbeat не приходит — завершить и запустить заново.</summary>
    RestartHung,
}

/// <summary>
/// Решает, когда (пере)запускать агента в сессии пользователя. Все моменты времени — по монотонным часам.
/// После запуска, который так и не дал heartbeat, следующая попытка откладывается: 10, 20, 40, 80, 120 с.
/// </summary>
public sealed class AgentWatchdog(TimeSpan firstSeen)
{
    TimeSpan? _lastLaunch;
    int _failedLaunches;

    public int FailedLaunches => _failedLaunches;

    public static TimeSpan BackoffFor(int failedLaunches) =>
        TimeSpan.FromSeconds(Math.Min(10 * Math.Pow(2, Math.Min(failedLaunches, 10)), 120));

    public AgentAction Decide(TimeSpan now, TimeSpan? lastHeartbeat, bool agentProcessRunning)
    {
        if (lastHeartbeat is { } hb && now - hb <= AccountingEngine.AgentTimeout)
        {
            if (_lastLaunch is null || hb >= _lastLaunch) _failedLaunches = 0;
            return AgentAction.None;
        }

        // Сессия только что появилась (вход в систему, старт службы) — даём агенту из автозагрузки время.
        if (now - firstSeen < AccountingEngine.AgentTimeout)
            return AgentAction.None;

        if (_lastLaunch is { } launched && (lastHeartbeat is null || lastHeartbeat < launched))
        {
            if (now - launched < BackoffFor(_failedLaunches))
                return AgentAction.None;
            _failedLaunches++;
        }

        return agentProcessRunning ? AgentAction.RestartHung : AgentAction.Launch;
    }

    public void OnLaunched(TimeSpan now) => _lastLaunch = now;
}
