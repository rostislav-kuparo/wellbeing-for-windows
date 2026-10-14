namespace Wellbeing.Core.Limits;

/// <param name="Thresholds">Пороги (в минутах), которые нужно пометить отправленными.</param>
public sealed record LimitWarning(Guid LimitId, string LimitName, long RemainingSeconds, IReadOnlyList<int> Thresholds);

/// <summary>
/// Предупреждения «осталось N минут». Срабатывают только для лимитов, которые расходуются прямо сейчас,
/// и только один раз на порог в день. Если пересечено сразу несколько порогов (например, лимит
/// уменьшили), приходит одно предупреждение. Об исчерпании лимита сообщает блокировщик, когда закрывает программу.
/// </summary>
public static class WarningPlanner
{
    public static IReadOnlyList<LimitWarning> Plan(
        IEnumerable<LimitStatus> statuses,
        IReadOnlyCollection<string> activeExes,
        IReadOnlyCollection<int> thresholdsMinutes,
        Func<Guid, int, bool> alreadySent)
    {
        var result = new List<LimitWarning>();
        if (activeExes.Count == 0 || thresholdsMinutes.Count == 0) return result;

        var active = activeExes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var status in statuses)
        {
            if (status.IsBlocked || !status.Rule.Exes.Any(active.Contains)) continue;

            // Остаток — меньшее из минут лимита и времени до конца разрешённого окна.
            var available = status.AvailableSeconds;
            var crossed = thresholdsMinutes
                .Where(t => t > 0 && available <= t * 60L && !alreadySent(status.Rule.Id, t))
                .Distinct()
                .ToList();
            if (crossed.Count > 0)
                result.Add(new LimitWarning(status.Rule.Id, status.Rule.Name, available, crossed));
        }

        return result;
    }
}
