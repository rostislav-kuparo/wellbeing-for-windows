namespace Wellbeing.Core.Time;

/// <summary>
/// Логический день: сутки начинаются не в полночь, а во время сброса (по умолчанию 04:00),
/// поэтому засиживание после полуночи считается тем же днём.
/// </summary>
public static class LogicalDay
{
    public static DateOnly Of(DateTime localTime, TimeOnly resetTime) =>
        DateOnly.FromDateTime(localTime - resetTime.ToTimeSpan());

    public static DateTime Start(DateOnly day, TimeOnly resetTime) => day.ToDateTime(resetTime);

    public static bool IsWeekend(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static DateOnly Parse(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
