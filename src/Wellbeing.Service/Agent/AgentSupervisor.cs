using Wellbeing.Core.Abstractions;
using Wellbeing.Core.Agent;
using Wellbeing.Service.Data;
using Wellbeing.Service.Tracking;

namespace Wellbeing.Service.Agent;

/// <summary>
/// Следит, чтобы в активной сессии пользователя работал агент. Если heartbeat-ов нет дольше 10 секунд,
/// завершает зависший экземпляр и запускает новый через WTSQueryUserToken + CreateProcessAsUser.
/// </summary>
public sealed class AgentSupervisor(
    ServiceOptions options,
    ISessionInfo sessions,
    IProcessSource processes,
    IProcessTerminator terminator,
    IUserProcessLauncher launcher,
    AgentRegistry agents,
    StateRepository state,
    IClock clock,
    ILogger<AgentSupervisor> log) : BackgroundService
{
    readonly Dictionary<int, AgentWatchdog> _watchdogs = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.ManageAgent)
        {
            log.LogInformation("Консольный режим: агент не запускается автоматически");
            return;
        }

        if (!File.Exists(options.AgentPath))
        {
            log.LogWarning("Агент не найден: {Path}", options.AgentPath);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    Check();
                }
                catch (Exception e)
                {
                    log.LogError(e, "Ошибка проверки агента");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void Check()
    {
        var session = sessions.ActiveConsoleSessionId;
        if (session <= 0) return;

        var now = clock.Monotonic;
        if (!_watchdogs.TryGetValue(session, out var watchdog))
        {
            if (!sessions.GetState(session).HasUser) return;
            _watchdogs[session] = watchdog = new AgentWatchdog(now);
        }

        var heartbeat = agents.Get(session);
        var alive = agents.IsAlive(session);
        List<ProcessEntry> running = alive
            ? []
            : processes.GetProcesses(exe => exe == ServiceOptions.AgentExe).Where(p => p.SessionId == session).ToList();

        var action = watchdog.Decide(now, heartbeat?.ReceivedAt, running.Count > 0);
        if (action == AgentAction.None) return;
        if (!sessions.GetState(session).HasUser) return;

        if (action == AgentAction.RestartHung)
            foreach (var process in running)
                terminator.TryTerminate(process.Pid);

        watchdog.OnLaunched(now);
        try
        {
            var pid = launcher.LaunchInSession(session, options.AgentPath, null);
            log.LogInformation("Агент запущен в сессии {Session}, PID {Pid}", session, pid);
            state.Log("agent", action == AgentAction.RestartHung ? "Агент не отвечал и был перезапущен" : "Агент запущен");
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Не удалось запустить агента в сессии {Session} (попытка {Attempt})", session, watchdog.FailedLaunches + 1);
        }
    }
}
