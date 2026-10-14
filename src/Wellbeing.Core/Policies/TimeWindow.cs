using System.Text.Json.Serialization;

namespace Wellbeing.Core.Policies;

/// <summary>
/// Разрешённое время суток [From, To). Может переходить через полночь: 20:00–01:00.
/// Отсутствие окна (null) — можно весь день. Окно с From == To — пустое (недоступно весь день); такое
/// получается только как пересечение двух непересекающихся окон, пользователь его задать не может.
/// </summary>
public sealed record TimeWindow(TimeOnly From, TimeOnly To)
{
    const int Day = 24 * 60 * 60;

    [JsonIgnore] public int LengthSeconds => Mod(Seconds(To) - Seconds(From));

    [JsonIgnore] public bool IsEmpty => LengthSeconds == 0;

    public bool Contains(TimeOnly time) => !IsEmpty && Offset(time) < LengthSeconds;

    /// <summary>Сколько секунд до конца окна (0, если сейчас вне окна).</summary>
    public int SecondsUntilEnd(TimeOnly time) => Contains(time) ? LengthSeconds - Offset(time) : 0;

    /// <summary>Окно целиком лежит внутри <paramref name="outer"/> (то есть не шире его).</summary>
    public bool IsWithin(TimeWindow outer)
    {
        if (IsEmpty) return true;
        if (outer.IsEmpty) return false;
        return Mod(Seconds(From) - Seconds(outer.From)) + LengthSeconds <= outer.LengthSeconds;
    }

    /// <summary>
    /// Пересечение. Если на круге суток оно распадается на два куска, берётся больший —
    /// он всё равно не шире ни одного из окон.
    /// </summary>
    public TimeWindow Intersect(TimeWindow other)
    {
        if (IsEmpty || other.IsEmpty) return new TimeWindow(From, From);

        // Разворачиваем круг от начала этого окна: это окно — [0, a), другое — [d, d + b).
        var a = LengthSeconds;
        var b = other.LengthSeconds;
        var d = Mod(Seconds(other.From) - Seconds(From));

        var (bestStart, bestLength) = (0, 0);
        if (d < a) (bestStart, bestLength) = (d, Math.Min(a, d + b) - d);
        if (d + b > Day)
        {
            var wrapped = Math.Min(a, d + b - Day);
            if (wrapped > bestLength) (bestStart, bestLength) = (0, wrapped);
        }

        var start = From.Add(TimeSpan.FromSeconds(bestStart));
        return new TimeWindow(start, start.Add(TimeSpan.FromSeconds(bestLength)));
    }

    public override string ToString() => $"{From:HH\\:mm}–{To:HH\\:mm}";

    /// <summary>null (весь день) внутри чего угодно только сам; любое окно внутри null.</summary>
    public static bool IsWithin(TimeWindow? inner, TimeWindow? outer) =>
        outer is null || (inner is not null && inner.IsWithin(outer));

    public static TimeWindow? Intersect(TimeWindow? a, TimeWindow? b) =>
        a is null ? b : b is null ? a : a.Intersect(b);

    int Offset(TimeOnly time) => Mod(Seconds(time) - Seconds(From));

    static int Seconds(TimeOnly time) => (int)(time.Ticks / TimeSpan.TicksPerSecond);

    static int Mod(int value) => ((value % Day) + Day) % Day;
}
