using System.Text.Json.Serialization;
using Wellbeing.Core.Time;

namespace Wellbeing.Core.Policies;

/// <summary>
/// Условие, по которому активность относится к лимиту. Сейчас поддерживается только <see cref="ExeType"/>.
/// <see cref="TitleType"/> зарезервирован под правила по заголовку окна (например, «YouTube» в chrome.exe):
/// заголовок активного окна уже приходит в heartbeat, а политика хранит матчеры списком с типом.
/// </summary>
public sealed record Matcher(string Type, string Exe, string? TitleContains = null)
{
    public const string ExeType = "exe";
    public const string TitleType = "title";

    public static Matcher ForExe(string exe) => new(ExeType, ExeName.Normalize(exe));
}

/// <summary>Лимит: одна программа или группа программ с общим временем.</summary>
public sealed record LimitRule
{
    /// <summary>Максимум минут — сутки. Он же означает «без лимита минут» (ограничено только окном времени).</summary>
    public const int MaxMinutes = 24 * 60;

    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<Matcher> Matchers { get; init; }
    public required int WeekdayMinutes { get; init; }

    /// <summary><c>null</c> — на выходных всё как в будни (и минуты, и разрешённое время).</summary>
    public int? WeekendMinutes { get; init; }

    /// <summary>Разрешённое время в будни; <c>null</c> — весь день.</summary>
    public TimeWindow? WeekdayWindow { get; init; }

    /// <summary>Разрешённое время в выходные — учитывается, только если для выходных заданы свои настройки.</summary>
    public TimeWindow? WeekendWindow { get; init; }

    [JsonIgnore] public bool HasSeparateWeekend => WeekendMinutes is not null;

    [JsonIgnore] public int EffectiveWeekendMinutes => WeekendMinutes ?? WeekdayMinutes;

    [JsonIgnore] public TimeWindow? EffectiveWeekendWindow => HasSeparateWeekend ? WeekendWindow : WeekdayWindow;

    [JsonIgnore]
    public IEnumerable<string> Exes => Matchers.Where(m => m.Type == Matcher.ExeType).Select(m => m.Exe).Distinct();

    public int MinutesFor(DateOnly day) => LogicalDay.IsWeekend(day) ? EffectiveWeekendMinutes : WeekdayMinutes;

    public TimeWindow? WindowFor(DateOnly day) => LogicalDay.IsWeekend(day) ? EffectiveWeekendWindow : WeekdayWindow;

    public static LimitRule Create(Guid id, string name, IEnumerable<string> exes, int weekdayMinutes, int? weekendMinutes = null,
        TimeWindow? weekdayWindow = null, TimeWindow? weekendWindow = null) => new()
    {
        Id = id,
        Name = name,
        Matchers = exes.Select(Matcher.ForExe).Distinct().ToList(),
        WeekdayMinutes = weekdayMinutes,
        WeekendMinutes = weekendMinutes,
        WeekdayWindow = weekdayWindow,
        WeekendWindow = weekendMinutes is null ? null : weekendWindow,
    };
}

/// <summary>Настройки, влияющие на учёт. Меняются по тем же правилам, что и лимиты.</summary>
public sealed record AccountingSettings(TimeOnly DayResetTime, int IdleThresholdSeconds)
{
    public static AccountingSettings Default { get; } = new(new TimeOnly(4, 0), 120);
}

/// <summary>Полный набор правил на день: лимиты и настройки учёта.</summary>
public sealed record Policy
{
    public static Policy Default { get; } = new() { Limits = [], Accounting = AccountingSettings.Default };

    public required IReadOnlyList<LimitRule> Limits { get; init; }
    public required AccountingSettings Accounting { get; init; }

    public LimitRule? Find(Guid id)
    {
        foreach (var limit in Limits)
            if (limit.Id == id) return limit;
        return null;
    }

    [JsonIgnore]
    public IReadOnlySet<string> LimitedExes =>
        Limits.SelectMany(l => l.Exes).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
