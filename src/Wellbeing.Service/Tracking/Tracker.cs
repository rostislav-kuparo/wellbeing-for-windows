using Wellbeing.Core.Abstractions;
using Wellbeing.Core.Limits;
using Wellbeing.Core.Policies;
using Wellbeing.Core.Safety;
using Wellbeing.Core.Text;
using Wellbeing.Core.Time;
using Wellbeing.Core.Tracking;
using Wellbeing.Service.Api;
using Wellbeing.Service.Data;

namespace Wellbeing.Service.Tracking;

/// <summary>
/// Сердце службы: раз в секунду решает, кому засчитать время, пересчитывает лимиты,
/// обновляет список блокировок и шлёт предупреждения.
/// </summary>
public sealed class Tracker(
    IClock clock,
    PolicyService policies,
    AgentRegistry agents,
    ISessionInfo sessions,
    IProcessSource processes,
    ProtectionRules protection,
    UsageRepository usage,
    StateRepository state,
    AppCatalog apps,
    NotificationHub notifications,
    SettingsService settings,
    EventBroadcaster events,
    ILogger<Tracker> log)
{
    static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);
    static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(2);
    static readonly IReadOnlyDictionary<string, LimitStatus> NothingBlocked = new Dictionary<string, LimitStatus>();

    readonly AccountingEngine _engine = new();
    readonly UsageLedger _ledger = new();
    readonly Lock _flushGate = new();

    DayClock _dayClock = new(DateOnly.MinValue);
    TimeSpan _startedAt, _lastTick, _lastFlush, _lastPublish, _lastTimeZoneRefresh;
    HashSet<(Guid LimitId, int Threshold)> _warningsSent = [];
    AttributionMode _lastMode = AttributionMode.None;
    volatile IReadOnlyDictionary<string, LimitStatus> _blocked = NothingBlocked;
    volatile TodayDto? _today;
    volatile bool _policyChanged;

    /// <summary>Заблокированные exe → лимит, который их блокирует (исчерпан или сейчас не разрешённое время).</summary>
    public IReadOnlyDictionary<string, LimitStatus> Blocked => _blocked;

    /// <summary>Срабатывает, когда меняется набор заблокированных программ.</summary>
    public event Action? BlockedChanged;

    public DateOnly CurrentDay => _dayClock.CurrentDay;

    public TodayDto? Today => _today;

    public IReadOnlyDictionary<string, long> UsageToday() => _ledger.SnapshotToday();

    public string TrayText
    {
        get
        {
            var total = _today?.TotalSeconds ?? 0;
            return $"Wellbeing — сегодня {DurationText.Format(total)}";
        }
    }

    public void Initialize()
    {
        var nowLocal = clock.ToLocal(clock.UtcNow);
        var saved = state.Get("current_day");
        var day = saved is not null
            ? LogicalDay.Parse(saved)
            : LogicalDay.Of(nowLocal, policies.Latest.Accounting.DayResetTime);

        _dayClock = new DayClock(day);
        _dayClock.Advance(nowLocal, ResetTimeFor);
        StartDay(_dayClock.CurrentDay);

        _startedAt = _lastTick = _lastFlush = _lastTimeZoneRefresh = clock.Monotonic;
        policies.Changed += () => _policyChanged = true;
        state.Log("service", "Служба запущена");
        log.LogInformation("Учёт запущен, логический день {Day}", LogicalDay.Format(_dayClock.CurrentDay));
    }

    public void Tick()
    {
        var mono = clock.Monotonic;
        var elapsed = mono - _lastTick;
        _lastTick = mono;

        if (mono - _lastTimeZoneRefresh > TimeSpan.FromMinutes(1))
        {
            TimeZoneInfo.ClearCachedData(); // пользователь мог сменить часовой пояс
            _lastTimeZoneRefresh = mono;
        }

        var nowUtc = clock.UtcNow;
        if (_dayClock.Advance(clock.ToLocal(nowUtc), ResetTimeFor))
        {
            Flush();
            StartDay(_dayClock.CurrentDay);
            state.Log("day", $"Начался новый день: {LogicalDay.Format(_dayClock.CurrentDay)}");
            state.PruneEvents(TimeSpan.FromDays(60));
        }

        var day = _dayClock.CurrentDay;
        var policy = policies.ForDay(day);
        var session = sessions.ActiveConsoleSessionId;
        var heartbeat = agents.Get(session);
        var sampleAge = heartbeat is null ? mono - _startedAt : mono - heartbeat.ReceivedAt;

        var result = _engine.Tick(new TickInput(
            elapsed, heartbeat?.Sample, sampleAge, policy.Accounting.IdleThresholdSeconds, () => Fallback(session, policy)));
        TrackModeChange(result);

        if (result.IsCounting)
            _ledger.Credit(day, nowUtc, result.Exes, result.Seconds);

        var statuses = LimitEvaluator.Evaluate(policy, day, _ledger.SnapshotToday(), TimeOnly.FromDateTime(clock.ToLocal(nowUtc)));
        var blockedChanged = UpdateBlocked(LimitEvaluator.BlockedExes(statuses));
        if (result.IsCounting)
            Warn(day, session, statuses, result.Exes);

        _today = BuildToday(day, policy, statuses, result, sampleAge);

        if (blockedChanged || _policyChanged || mono - _lastPublish >= PublishInterval)
        {
            _policyChanged = false;
            _lastPublish = mono;
            events.Publish("today", _today);
        }

        if (mono - _lastFlush >= FlushInterval)
        {
            _lastFlush = mono;
            Flush();
        }
    }

    /// <summary>Записывает накопленное в БД. Вызывается по таймеру и перед запросами истории.</summary>
    public void Flush()
    {
        lock (_flushGate)
        {
            var pending = _ledger.DrainPending();
            try
            {
                usage.Add(pending);
                apps.FlushLastSeen();
            }
            catch (Exception e)
            {
                _ledger.Restore(pending);
                log.LogError(e, "Не удалось записать учёт в БД");
            }
        }
    }

    TimeOnly ResetTimeFor(DateOnly day) => policies.ForDay(day).Accounting.DayResetTime;

    void StartDay(DateOnly day)
    {
        _ledger.StartDay(day, usage.TotalsForDay(day));
        _warningsSent = state.WarningsSent(day);
        state.Set("current_day", LogicalDay.Format(day));
    }

    FallbackSnapshot Fallback(int session, Policy policy)
    {
        if (session < 0) return FallbackSnapshot.NoUser;
        var sessionState = sessions.GetState(session);
        if (!sessionState.HasUser) return FallbackSnapshot.NoUser;

        var limited = policy.LimitedExes;
        if (limited.Count == 0) return new FallbackSnapshot(true, sessionState.IsLocked, []);

        var running = processes.GetProcesses(limited.Contains)
            .Where(p => p.SessionId == session && protection.MayTerminate(p.SessionId, p.Exe, p.Path))
            .Select(p => p.Exe)
            .ToList();
        return new FallbackSnapshot(true, sessionState.IsLocked, running);
    }

    void TrackModeChange(TickResult result)
    {
        // Без вошедшего пользователя агенту негде работать — это не «агент не отвечает».
        var mode = result.Pause == PauseReason.NoUser ? AttributionMode.None : result.Mode;
        if (mode == _lastMode || mode == AttributionMode.None) return;
        if (mode == AttributionMode.Fallback)
        {
            state.Log("agent", "Агент не отвечает — время идёт всем запущенным программам с лимитом");
            log.LogWarning("Агент не отвечает, включён учёт без агента");
        }
        else if (_lastMode == AttributionMode.Fallback)
        {
            state.Log("agent", "Агент снова на связи");
        }

        _lastMode = mode;
    }

    bool UpdateBlocked(IReadOnlyDictionary<string, LimitStatus> blocked)
    {
        var previous = _blocked;
        _blocked = blocked.Count == 0 ? NothingBlocked : blocked; // причина могла смениться и при том же наборе exe
        if (blocked.Count == previous.Count && blocked.Keys.All(previous.ContainsKey)) return false;

        var newlyBlocked = blocked.Where(b => !previous.ContainsKey(b.Key)).Select(b => b.Value).DistinctBy(s => s.Rule.Id);
        foreach (var status in newlyBlocked)
            state.Log("limit", BlockText.Journal(status));

        BlockedChanged?.Invoke();
        return true;
    }

    void Warn(DateOnly day, int session, IReadOnlyList<LimitStatus> statuses, IReadOnlyList<string> activeExes)
    {
        var notificationSettings = settings.Notifications;
        if (!notificationSettings.WarningsEnabled) return;

        var warnings = WarningPlanner.Plan(statuses, activeExes, notificationSettings.WarnMinutes,
            (id, threshold) => _warningsSent.Contains((id, threshold)));
        foreach (var warning in warnings)
        {
            foreach (var threshold in warning.Thresholds)
            {
                _warningsSent.Add((warning.LimitId, threshold));
                state.MarkWarningSent(day, warning.LimitId, threshold);
            }

            notifications.Notify(session,
                $"Осталось {DurationText.RemainingMinutes(warning.RemainingSeconds)}: {warning.LimitName}",
                "Когда лимит закончится, программа будет закрыта. Сохраните прогресс.",
                important: false);
        }
    }

    TodayDto BuildToday(DateOnly day, Policy today, IReadOnlyList<LimitStatus> statuses, TickResult tick, TimeSpan agentAge)
    {
        var usageByExe = _ledger.SnapshotToday();
        var pending = PolicyBook.Diff(today, policies.Latest)
            .Where(c => c.HasPendingChange)
            .Select(c => c.Id)
            .ToHashSet();

        var limits = statuses.Select(s => new LimitProgressDto(
            s.Rule.Id, s.Rule.Name, s.Rule.Exes.ToList(), s.AllowedSeconds, s.UsedSeconds, s.RemainingSeconds,
            s.IsExhausted, pending.Contains(s.Rule.Id), Dto.Reason(s.BlockReason), s.AvailableSeconds,
            Dto.Window(s.Window), s.WindowSecondsLeft)).ToList();

        var appsToday = usageByExe
            .Where(u => u.Value > 0)
            .OrderByDescending(u => u.Value)
            .Select(u =>
            {
                var app = apps.Get(u.Key);
                return new AppUsageDto(u.Key, app?.DisplayName ?? u.Key, u.Value, app?.HasIcon == true);
            })
            .ToList();

        var currentExe = tick.Exes.Count == 1 ? tick.Exes[0] : null;
        var current = new CurrentActivityDto(
            currentExe,
            currentExe is null ? null : apps.DisplayName(currentExe),
            tick.Pause switch
            {
                PauseReason.None => tick.Exes.Count > 0 ? "counting" : "noWindow",
                PauseReason.Idle => "idle",
                PauseReason.Locked => "locked",
                PauseReason.NoWindow => "noWindow",
                PauseReason.NoUser => "noUser",
                PauseReason.SleepGap => "sleep",
                _ => "waiting",
            },
            tick.Mode switch
            {
                AttributionMode.Agent => "agent",
                AttributionMode.AgentGrace => "grace",
                AttributionMode.Fallback => "fallback",
                _ => "none",
            });

        var agentStatus = agentAge <= AccountingEngine.FreshWindow ? "ok"
            : agentAge <= AccountingEngine.AgentTimeout ? "late"
            : "missing";

        return new TodayDto(
            LogicalDay.Format(day),
            policies.DayStartsAt(day),
            policies.TomorrowStartsAt(day),
            usageByExe.Values.Sum(),
            limits,
            appsToday,
            current,
            new AgentStatusDto(agentStatus, Math.Round(agentAge.TotalSeconds, 1)));
    }
}
