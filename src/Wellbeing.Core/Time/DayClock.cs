namespace Wellbeing.Core.Time;

/// <summary>
/// Текущий логический день.
/// <list type="bullet">
/// <item>День D заканчивается в (D+1) + время сброса, действующее в день D. Поэтому изменённое сегодня
/// время сброса влияет только на следующую границу и не может «досрочно» начать новый день.</item>
/// <item>День никогда не откатывается назад (перевод часов или часового пояса назад ничего не ломает).</item>
/// <item>После долгого простоя (компьютер был выключен) день перескакивает сразу на нужный.</item>
/// </list>
/// </summary>
public sealed class DayClock(DateOnly currentDay)
{
    public DateOnly CurrentDay { get; private set; } = currentDay;

    public static DateTime EndOf(DateOnly day, TimeOnly resetTimeOfDay) => day.AddDays(1).ToDateTime(resetTimeOfDay);

    /// <param name="nowLocal">Текущее локальное время.</param>
    /// <param name="resetTimeForDay">Время сброса по политике, действующей в указанный день.</param>
    /// <returns><c>true</c>, если начался новый день.</returns>
    public bool Advance(DateTime nowLocal, Func<DateOnly, TimeOnly> resetTimeForDay)
    {
        var current = CurrentDay;
        if (nowLocal < EndOf(current, resetTimeForDay(current)))
            return false;

        // Все дни после текущего живут по одной политике (ревизии создаются максимум на завтра),
        // поэтому достаточно времени сброса завтрашнего дня.
        var next = current.AddDays(1);
        var natural = LogicalDay.Of(nowLocal, resetTimeForDay(next));
        CurrentDay = natural > next ? natural : next;
        return true;
    }
}
