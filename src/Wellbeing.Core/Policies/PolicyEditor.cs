namespace Wellbeing.Core.Policies;

public enum ChangeTiming
{
    /// <summary>Ничего не изменилось.</summary>
    None,

    /// <summary>Всё применено сразу (ужесточение или косметика).</summary>
    Immediate,

    /// <summary>Всё вступит в силу завтра (ослабление).</summary>
    Deferred,

    /// <summary>Ужесточающая часть применена сразу, остальное — завтра.</summary>
    Partial,
}

/// <summary>Какие ревизии записать: действующую с сегодня и/или с завтра.</summary>
public sealed record PolicyChangePlan(Policy? ApplyToday, Policy? ApplyTomorrow, ChangeTiming Timing)
{
    public static PolicyChangePlan Nothing { get; } = new(null, null, ChangeTiming.None);
}

/// <summary>
/// Правило «ужесточение — сразу, ослабление — с завтра».
/// <para>
/// Если новая политика не мягче сегодняшней — она действует с сегодня. Иначе пишутся две ревизии:
/// на сегодня — самое строгое из сегодняшней и новой (так ужесточающая часть изменения применяется сразу),
/// на завтра — новая политика целиком.
/// </para>
/// </summary>
public static class PolicyEditor
{
    /// <param name="today">Политика, действующая сегодня.</param>
    /// <param name="latest">Последняя записанная политика (с учётом запланированного на завтра) — от неё считается правка.</param>
    /// <param name="proposed">Желаемая политика.</param>
    public static PolicyChangePlan Plan(Policy today, Policy latest, Policy proposed)
    {
        if (PolicyMath.AreIdentical(proposed, latest))
            return PolicyChangePlan.Nothing;

        if (PolicyMath.IsAtLeastAsStrict(proposed, today))
            return new PolicyChangePlan(proposed, null, ChangeTiming.Immediate);

        var strictest = PolicyMath.Strictest(today, proposed);
        if (PolicyMath.AreIdentical(strictest, today))
            return new PolicyChangePlan(null, proposed, ChangeTiming.Deferred);

        // Сегодняшняя ревизия может отличаться от текущей только названием — это не «частичное применение».
        var timing = PolicyMath.IsAtLeastAsStrict(today, strictest) ? ChangeTiming.Deferred : ChangeTiming.Partial;
        return new PolicyChangePlan(strictest, proposed, timing);
    }

    /// <summary>
    /// Когда вступит в силу изменение конкретного лимита. Общий <see cref="PolicyChangePlan.Timing"/> для этого
    /// не годится: ослабление другого лимита, запланированное раньше, не делает новое ужесточение «частичным».
    /// </summary>
    public static ChangeTiming TimingFor(Guid limitId, Policy todayBefore, Policy latestBefore, PolicyChangePlan plan)
    {
        if (plan.Timing == ChangeTiming.None) return ChangeTiming.None;
        var before = todayBefore.Find(limitId);
        var todayAfter = (plan.ApplyToday ?? todayBefore).Find(limitId);
        var tomorrowAfter = (plan.ApplyTomorrow ?? plan.ApplyToday ?? latestBefore).Find(limitId);

        if (PolicyMath.AreIdentical(todayAfter, tomorrowAfter)) return ChangeTiming.Immediate;
        return EquallyStrict(todayAfter, before) ? ChangeTiming.Deferred : ChangeTiming.Partial;
    }

    /// <summary>То же для настроек учёта (время сброса, порог бездействия).</summary>
    public static ChangeTiming TimingForAccounting(Policy todayBefore, Policy latestBefore, PolicyChangePlan plan)
    {
        if (plan.Timing == ChangeTiming.None) return ChangeTiming.None;
        var todayAfter = (plan.ApplyToday ?? todayBefore).Accounting;
        var tomorrowAfter = (plan.ApplyTomorrow ?? plan.ApplyToday ?? latestBefore).Accounting;

        if (todayAfter == tomorrowAfter) return ChangeTiming.Immediate;
        return todayAfter == todayBefore.Accounting ? ChangeTiming.Deferred : ChangeTiming.Partial;
    }

    static bool EquallyStrict(LimitRule? a, LimitRule? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return PolicyMath.IsAtLeastAsStrict(a, b) && PolicyMath.IsAtLeastAsStrict(b, a);
    }
}
