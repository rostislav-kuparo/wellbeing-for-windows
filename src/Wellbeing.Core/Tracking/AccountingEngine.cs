namespace Wellbeing.Core.Tracking;

/// <summary>Что агент видит в сессии пользователя.</summary>
public sealed record ActivitySample(string? Exe, string? WindowTitle, int IdleSeconds, bool IsFullscreen, bool IsLocked);

/// <summary>Что служба видит сама, когда агента нет: запущенные программы с лимитом в активной сессии.</summary>
public sealed record FallbackSnapshot(bool HasUser, bool IsLocked, IReadOnlyCollection<string> RunningLimitedExes)
{
    public static FallbackSnapshot NoUser { get; } = new(false, false, []);
}

public enum AttributionMode
{
    None,

    /// <summary>Свежий heartbeat от агента.</summary>
    Agent,

    /// <summary>Heartbeat задерживается (3–10 с): считаем по последнему известному окну.</summary>
    AgentGrace,

    /// <summary>Агента нет дольше 10 с: считаем все запущенные программы с лимитом.</summary>
    Fallback,
}

public enum PauseReason
{
    None,
    Idle,
    Locked,
    NoWindow,
    NoUser,
    SleepGap,
    WaitingForAgent,
}

/// <param name="Elapsed">Сколько прошло с прошлого тика по монотонным часам.</param>
/// <param name="Sample">Последний heartbeat активной сессии.</param>
/// <param name="SampleAge">Сколько прошло с последнего heartbeat (или со старта службы, если его ещё не было).</param>
/// <param name="Fallback">Вызывается, только если агент молчит дольше <see cref="AccountingEngine.AgentTimeout"/>.</param>
public readonly record struct TickInput(
    TimeSpan Elapsed,
    ActivitySample? Sample,
    TimeSpan SampleAge,
    int IdleThresholdSeconds,
    Func<FallbackSnapshot> Fallback);

public sealed record TickResult(AttributionMode Mode, PauseReason Pause, int Seconds, IReadOnlyList<string> Exes)
{
    public bool IsCounting => Pause == PauseReason.None && Exes.Count > 0;
}

/// <summary>
/// Решает, какой программе засчитать прошедшие секунды. Вызывается службой раз в секунду.
/// <list type="bullet">
/// <item>Считается только активное окно, и только если сессия не заблокирована и ввод был недавно
/// (или окно полноэкранное — видео, игры).</item>
/// <item>Разрыв между тиками больше <see cref="MaxTickGap"/> (сон, гибернация) не засчитывается.</item>
/// <item>Если агент не отвечает дольше <see cref="AgentTimeout"/>, время идёт всем запущенным программам с лимитом —
/// иначе завершение агента давало бы бесплатное время.</item>
/// </list>
/// </summary>
public sealed class AccountingEngine
{
    public static TimeSpan FreshWindow { get; } = TimeSpan.FromSeconds(3);
    public static TimeSpan AgentTimeout { get; } = TimeSpan.FromSeconds(10);
    public static TimeSpan MaxTickGap { get; } = TimeSpan.FromSeconds(5);

    long _carryMs;

    public TickResult Tick(in TickInput input)
    {
        if (input.Elapsed > MaxTickGap)
        {
            _carryMs = 0;
            return Paused(AttributionMode.None, PauseReason.SleepGap);
        }

        // Тики идут неровно (1000 ± несколько мс), поэтому копим миллисекунды и отдаём целые секунды.
        _carryMs += Math.Max(0, (long)input.Elapsed.TotalMilliseconds);
        var seconds = (int)(_carryMs / 1000);
        _carryMs -= seconds * 1000L;

        if (input.SampleAge <= AgentTimeout)
        {
            if (input.Sample is not { } sample)
                return Paused(AttributionMode.None, PauseReason.WaitingForAgent);

            var mode = input.SampleAge <= FreshWindow ? AttributionMode.Agent : AttributionMode.AgentGrace;
            if (sample.IsLocked) return Paused(mode, PauseReason.Locked);
            if (string.IsNullOrEmpty(sample.Exe)) return Paused(mode, PauseReason.NoWindow);
            if (sample.IdleSeconds >= input.IdleThresholdSeconds && !sample.IsFullscreen)
                return new TickResult(mode, PauseReason.Idle, 0, [sample.Exe]);
            return new TickResult(mode, PauseReason.None, seconds, [sample.Exe]);
        }

        var fallback = input.Fallback();
        if (!fallback.HasUser) return Paused(AttributionMode.Fallback, PauseReason.NoUser);
        if (fallback.IsLocked) return Paused(AttributionMode.Fallback, PauseReason.Locked);
        var exes = fallback.RunningLimitedExes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new TickResult(AttributionMode.Fallback, PauseReason.None, seconds, exes);
    }

    static TickResult Paused(AttributionMode mode, PauseReason reason) => new(mode, reason, 0, []);
}
