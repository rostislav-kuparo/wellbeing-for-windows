namespace Wellbeing.Core.Policies;

/// <summary>Сравнение политик по строгости. Основа правила «ужесточение — сразу, ослабление — с завтра».</summary>
public static class PolicyMath
{
    /// <summary>
    /// <paramref name="candidate"/> не мягче <paramref name="baseline"/>: каждый лимит baseline есть в candidate,
    /// охватывает не меньше программ, даёт не больше минут и не более широкое разрешённое время ни в будни,
    /// ни в выходные; порог бездействия не меньше; время сброса дня то же.
    /// </summary>
    public static bool IsAtLeastAsStrict(Policy candidate, Policy baseline)
    {
        foreach (var b in baseline.Limits)
        {
            var c = candidate.Find(b.Id);
            if (c is null || !IsAtLeastAsStrict(c, b)) return false;
        }

        return candidate.Accounting.IdleThresholdSeconds >= baseline.Accounting.IdleThresholdSeconds
               && candidate.Accounting.DayResetTime == baseline.Accounting.DayResetTime;
    }

    public static bool IsAtLeastAsStrict(LimitRule candidate, LimitRule baseline) =>
        candidate.WeekdayMinutes <= baseline.WeekdayMinutes
        && candidate.EffectiveWeekendMinutes <= baseline.EffectiveWeekendMinutes
        && TimeWindow.IsWithin(candidate.WeekdayWindow, baseline.WeekdayWindow)
        && TimeWindow.IsWithin(candidate.EffectiveWeekendWindow, baseline.EffectiveWeekendWindow)
        && baseline.Matchers.All(candidate.Matchers.Contains);

    /// <summary>
    /// Самая строгая комбинация двух политик: минимум минут, пересечение разрешённого времени, объединение
    /// программ в группах, лимиты из обеих политик, максимальный порог бездействия. Время сброса — текущее.
    /// </summary>
    public static Policy Strictest(Policy current, Policy proposed)
    {
        var limits = new List<LimitRule>(current.Limits.Count + proposed.Limits.Count);
        foreach (var cur in current.Limits)
        {
            var prop = proposed.Find(cur.Id);
            limits.Add(prop is null ? cur : Strictest(cur, prop));
        }

        limits.AddRange(proposed.Limits.Where(p => current.Find(p.Id) is null));

        return new Policy
        {
            Limits = limits,
            Accounting = current.Accounting with
            {
                IdleThresholdSeconds = Math.Max(current.Accounting.IdleThresholdSeconds, proposed.Accounting.IdleThresholdSeconds),
            },
        };
    }

    public static LimitRule Strictest(LimitRule current, LimitRule proposed)
    {
        var separateWeekend = current.HasSeparateWeekend || proposed.HasSeparateWeekend;
        return proposed with
        {
            Matchers = current.Matchers.Concat(proposed.Matchers).Distinct().ToList(),
            WeekdayMinutes = Math.Min(current.WeekdayMinutes, proposed.WeekdayMinutes),
            WeekendMinutes = separateWeekend ? Math.Min(current.EffectiveWeekendMinutes, proposed.EffectiveWeekendMinutes) : null,
            WeekdayWindow = TimeWindow.Intersect(current.WeekdayWindow, proposed.WeekdayWindow),
            WeekendWindow = separateWeekend ? TimeWindow.Intersect(current.EffectiveWeekendWindow, proposed.EffectiveWeekendWindow) : null,
        };
    }

    public static bool AreIdentical(Policy a, Policy b)
    {
        if (a.Accounting != b.Accounting || a.Limits.Count != b.Limits.Count) return false;
        for (var i = 0; i < a.Limits.Count; i++)
            if (!AreIdentical(a.Limits[i], b.Limits[i])) return false;
        return true;
    }

    public static bool AreIdentical(LimitRule? a, LimitRule? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.Id == b.Id
               && a.Name == b.Name
               && a.WeekdayMinutes == b.WeekdayMinutes
               && a.WeekendMinutes == b.WeekendMinutes
               && a.WeekdayWindow == b.WeekdayWindow
               && a.WeekendWindow == b.WeekendWindow
               && a.Matchers.SequenceEqual(b.Matchers);
    }
}
