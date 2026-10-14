using System.Collections.Concurrent;
using Wellbeing.Core;
using Wellbeing.Core.Abstractions;
using Wellbeing.Core.Limits;
using Wellbeing.Core.Safety;
using Wellbeing.Core.Text;
using Wellbeing.Service.Api;
using Wellbeing.Service.Data;
using Wellbeing.Service.Tracking;

namespace Wellbeing.Service.Blocking;

/// <summary>
/// Жёсткая блокировка: завершает программы с исчерпанным лимитом или вне разрешённого времени.
/// Новые запуски ловит WMI-событие Win32_ProcessStartTrace; раз в 5 секунд — страховочная проверка
/// (на случай потерянного события и сразу после старта службы).
/// </summary>
public sealed class ProcessGuard : BackgroundService
{
    static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);
    static readonly TimeSpan NotifyThrottle = TimeSpan.FromSeconds(20);

    readonly Tracker _tracker;
    readonly IProcessSource _processes;
    readonly IProcessTerminator _terminator;
    readonly IProcessStartWatcher _watcher;
    readonly ProtectionRules _protection;
    readonly NotificationHub _notifications;
    readonly AppCatalog _apps;
    readonly StateRepository _state;
    readonly EventBroadcaster _events;
    readonly IClock _clock;
    readonly ILogger<ProcessGuard> _log;
    readonly SemaphoreSlim _sweepNow = new(0);
    readonly ConcurrentDictionary<string, TimeSpan> _lastNotified = new(StringComparer.OrdinalIgnoreCase);

    public ProcessGuard(Tracker tracker, IProcessSource processes, IProcessTerminator terminator, IProcessStartWatcher watcher,
        ProtectionRules protection, NotificationHub notifications, AppCatalog apps, StateRepository state,
        EventBroadcaster events, IClock clock, ILogger<ProcessGuard> log)
    {
        _tracker = tracker;
        _processes = processes;
        _terminator = terminator;
        _watcher = watcher;
        _protection = protection;
        _notifications = notifications;
        _apps = apps;
        _state = state;
        _events = events;
        _clock = clock;
        _log = log;
        _tracker.BlockedChanged += () => _sweepNow.Release();
    }

    /// <summary>Работают ли мгновенные события о запуске процессов (нужны права администратора).</summary>
    public bool StartEventsActive { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _watcher.ProcessStarted += OnProcessStarted;
            _watcher.Start();
            StartEventsActive = true;
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "События запуска процессов недоступны (нужны права администратора) — работает только проверка раз в 5 с");
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _sweepNow.WaitAsync(SweepInterval, stoppingToken);
                Sweep();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override void Dispose()
    {
        _watcher.Dispose();
        base.Dispose();
    }

    void OnProcessStarted(int pid, int sessionId, string name)
    {
        var blocked = _tracker.Blocked;
        if (blocked.Count == 0 || sessionId <= 0) return;

        // Имя в событии может быть урезано до 15 символов — тогда уточняем по PID.
        var eventName = string.IsNullOrEmpty(name) ? null : ExeName.Normalize(name);
        if (eventName is not null && eventName.Length < 15 && !blocked.ContainsKey(eventName)) return;

        var process = _processes.TryGet(pid);
        if (process is not null && blocked.TryGetValue(process.Exe, out var limit))
            Enforce(process, limit);
    }

    void Sweep()
    {
        var blocked = _tracker.Blocked;
        if (blocked.Count == 0) return;
        try
        {
            foreach (var process in _processes.GetProcesses(blocked.ContainsKey))
                if (blocked.TryGetValue(process.Exe, out var limit))
                    Enforce(process, limit);
        }
        catch (Exception e)
        {
            _log.LogError(e, "Ошибка проверки процессов");
        }
    }

    void Enforce(ProcessEntry process, LimitStatus status)
    {
        if (!_protection.MayTerminate(process.SessionId, process.Exe, process.Path)) return;
        if (!_terminator.TryTerminate(process.Pid))
        {
            _log.LogDebug("Не удалось завершить {Exe} (PID {Pid})", process.Exe, process.Pid);
            return;
        }

        var key = $"{process.SessionId}:{process.Exe}";
        var now = _clock.Monotonic;
        if (_lastNotified.TryGetValue(key, out var last) && now - last < NotifyThrottle) return;
        _lastNotified[key] = now;

        var name = _apps.DisplayName(process.Exe);
        var reason = BlockText.Reason(status);
        _log.LogInformation("Завершена {Exe}: {Reason}", process.Exe, reason);
        _state.Log("blocked", $"{name} закрыта: {reason}");
        _notifications.Notify(process.SessionId, BlockText.Title(status), $"{name} закрыта — {reason}.", important: true);
        _events.Publish("app-blocked", new { exe = process.Exe, displayName = name, limit = status.Rule.Name, reason });
    }
}
