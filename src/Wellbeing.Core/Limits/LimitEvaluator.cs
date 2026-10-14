using Wellbeing.Core.Policies;

namespace Wellbeing.Core.Limits;

public enum BlockReason
{
    None,

    /// <summary>Минуты на сегодня закончились.</summary>
    Exhausted,

    /// <summary>Сейчас не разрешённое время (например, вне 18:00–22:00).</summary>
    OutsideWindow,
}

/// <param name="Window">Разрешённое время на этот день; null — весь день.</param>
/// <param name="InsideWindow">Сейчас разрешённое время (всегда true без окна).</param>
/// <param name="WindowSecondsLeft">Сколько осталось до конца окна; null — окна нет.</param>
public sealed record LimitStatus(
    LimitRule Rule,
    int AllowedSeconds,
    long UsedSeconds,
    TimeWindow? Window = null,
    bool InsideWindow = true,
    long? WindowSecondsLeft = null)
{
    public long RemainingSeconds => Math.Max(0, AllowedSeconds - UsedSeconds);
    public bool IsExhausted => UsedSeconds >= AllowedSeconds;

    public BlockReason BlockReason =>
        IsExhausted ? BlockReason.Exhausted : !InsideWindow ? BlockReason.OutsideWindow : BlockReason.None;

    public bool IsBlocked => BlockReason != BlockReason.None;

    /// <summary>Сколько ещё можно прямо сейчас: меньшее из остатка минут и времени до конца окна.</summary>
    public long AvailableSeconds => IsBlocked ? 0 : Math.Min(RemainingSeconds, WindowSecondsLeft ?? long.MaxValue);
}

public static class LimitEvaluator
{
    /// <summary>
    /// Использование лимита — сумма секунд его программ за логический день. Поэтому программа, добавленная
    /// в группу днём, сразу приносит в неё своё сегодняшнее время. Разрешённое время сверяется с часами.
    /// </summary>
    public static IReadOnlyList<LimitStatus> Evaluate(Policy policy, DateOnly day, IReadOnlyDictionary<string, long> usageSecondsByExe, TimeOnly now) =>
        policy.Limits
            .Select(rule =>
            {
                var window = rule.WindowFor(day);
                return new LimitStatus(
                    rule,
                    rule.MinutesFor(day) * 60,
                    rule.Exes.Sum(exe => usageSecondsByExe.GetValueOrDefault(exe)),
                    window,
                    window is null || window.Contains(now),
                    window?.SecondsUntilEnd(now));
            })
            .ToList();

    /// <summary>exe → лимит, который его сейчас блокирует (исчерпанный — в приоритете перед «вне окна»).</summary>
    public static IReadOnlyDictionary<string, LimitStatus> BlockedExes(IEnumerable<LimitStatus> statuses)
    {
        var result = new Dictionary<string, LimitStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in statuses.Where(s => s.IsBlocked).OrderBy(s => s.BlockReason == BlockReason.Exhausted ? 0 : 1))
            foreach (var exe in status.Rule.Exes)
                result.TryAdd(exe, status);
        return result;
    }
}
