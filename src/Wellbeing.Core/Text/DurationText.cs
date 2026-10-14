namespace Wellbeing.Core.Text;

public static class DurationText
{
    /// <summary>«2 ч 05 мин», «23 мин», «2 ч».</summary>
    public static string Format(long seconds)
    {
        if (seconds <= 0) return "0 мин";
        if (seconds < 60) return "< 1 мин";
        var totalMinutes = seconds / 60;
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0) return $"{minutes} мин";
        return minutes == 0 ? $"{hours} ч" : $"{hours} ч {minutes:00} мин";
    }

    /// <summary>Остаток с округлением вверх: 61 с → «2 мин». Для предупреждений «осталось N мин».</summary>
    public static string RemainingMinutes(long seconds) => $"{Math.Max(0, (seconds + 59) / 60)} мин";
}
