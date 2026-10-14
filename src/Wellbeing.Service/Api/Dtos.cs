using System.Globalization;
using Wellbeing.Core.Limits;
using Wellbeing.Core.Policies;

namespace Wellbeing.Service.Api;

/// <summary>Разрешённое время в API: «18:00»–«22:00».</summary>
public sealed record TimeWindowDto(string From, string To);

public static class Dto
{
    public static TimeWindowDto? Window(TimeWindow? window) =>
        window is null ? null : new TimeWindowDto(Time(window.From), Time(window.To));

    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <param name="error">Текст ошибки, если строка времени некорректна.</param>
    public static TimeWindow? ParseWindow(TimeWindowDto? dto, out string? error)
    {
        error = null;
        if (dto is null) return null;
        if (TimeOnly.TryParseExact(dto.From, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            && TimeOnly.TryParseExact(dto.To, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            return new TimeWindow(from, to);
        error = "Разрешённое время задаётся в формате ЧЧ:ММ";
        return null;
    }

    /// <summary>none | exhausted | outsideWindow</summary>
    public static string Reason(BlockReason reason) => reason switch
    {
        BlockReason.Exhausted => "exhausted",
        BlockReason.OutsideWindow => "outsideWindow",
        _ => "none",
    };
}

public sealed record TodayDto(
    string Day,
    DateTimeOffset DayStart,
    DateTimeOffset DayEnd,
    long TotalSeconds,
    IReadOnlyList<LimitProgressDto> Limits,
    IReadOnlyList<AppUsageDto> Apps,
    CurrentActivityDto Current,
    AgentStatusDto Agent);

public sealed record LimitProgressDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Exes,
    int AllowedSeconds,
    long UsedSeconds,
    long RemainingSeconds,
    bool Exhausted,
    bool HasPendingChange,
    string BlockReason,
    long AvailableSeconds,
    TimeWindowDto? Window,
    long? WindowSecondsLeft);

public sealed record AppUsageDto(string Exe, string DisplayName, long Seconds, bool HasIcon);

/// <param name="State">counting | idle | locked | noWindow | noUser | sleep | waiting</param>
/// <param name="Mode">agent | grace | fallback | none</param>
public sealed record CurrentActivityDto(string? Exe, string? DisplayName, string State, string Mode);

/// <param name="Status">ok | late | missing</param>
public sealed record AgentStatusDto(string Status, double? LastSeenSecondsAgo);

public sealed record TimelineDto(string Day, DateTimeOffset DayStart, DateTimeOffset DayEnd, IReadOnlyList<TimelineEntryDto> Minutes);

/// <param name="T">Начало минуты, unix-секунды.</param>
/// <param name="S">Секунд активности в этой минуте.</param>
public sealed record TimelineEntryDto(long T, string Exe, long S);

public sealed record DailyDto(IReadOnlyList<DayUsageDto> Days, IReadOnlyDictionary<string, string> Names);

public sealed record DayUsageDto(string Day, long TotalSeconds, IReadOnlyDictionary<string, long> Apps);

public sealed record AppDto(
    string Exe,
    string DisplayName,
    string? Path,
    DateTimeOffset LastSeen,
    long SecondsLast7Days,
    bool HasIcon,
    bool CanLimit,
    string? CannotLimitReason);

public sealed record RegisterAppInput(string Path);

public sealed record LimitConfigDto(string Name, IReadOnlyList<string> Exes, int WeekdayMinutes, int? WeekendMinutes, TimeWindowDto? WeekdayWindow, TimeWindowDto? WeekendWindow);

public sealed record LimitItemDto(
    Guid Id,
    LimitConfigDto? Today,
    LimitConfigDto? Tomorrow,
    bool HasPendingChange,
    bool PendingDelete,
    int AllowedSecondsToday,
    long UsedSeconds,
    long RemainingSeconds,
    bool Exhausted,
    string BlockReason,
    long AvailableSeconds,
    TimeWindowDto? WindowToday);

public sealed record LimitsDto(string Day, DateTimeOffset TomorrowStartsAt, IReadOnlyList<LimitItemDto> Limits);

public sealed record LimitInput(string? Name, IReadOnlyList<string> Exes, int WeekdayMinutes, int? WeekendMinutes, TimeWindowDto? WeekdayWindow = null, TimeWindowDto? WeekendWindow = null);

/// <param name="Timing">none | immediate | deferred | partial</param>
public sealed record ChangeResultDto(string Timing, DateTimeOffset TomorrowStartsAt, string Message, Guid? Id = null);

public sealed record AccountingDto(string DayResetTime, int IdleThresholdMinutes);

public sealed record NotificationSettingsDto(bool WarningsEnabled, IReadOnlyList<int> WarnMinutes);

public sealed record ServiceInfoDto(string Version, string Mode, string DataDirectory, bool ProcessStartEvents);

public sealed record SettingsDto(
    AccountingDto Today,
    AccountingDto? Tomorrow,
    NotificationSettingsDto Notifications,
    IReadOnlyList<int> AllowedWarnMinutes,
    DateTimeOffset TomorrowStartsAt,
    ServiceInfoDto Service);

public sealed record SettingsInput(AccountingDto? Accounting, NotificationSettingsDto? Notifications);

public sealed record SettingsChangeDto(ChangeResultDto? Accounting, SettingsDto Settings);

public sealed record ErrorsDto(IReadOnlyList<string> Errors);
