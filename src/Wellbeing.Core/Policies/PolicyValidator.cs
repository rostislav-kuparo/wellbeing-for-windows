using Wellbeing.Core.Safety;

namespace Wellbeing.Core.Policies;

public static class PolicyValidator
{
    public const int MinIdleThresholdSeconds = 60;
    public const int MaxIdleThresholdSeconds = 60 * 60;

    /// <param name="knownPath">Путь к exe, если он известен (из каталога программ).</param>
    /// <param name="existsInSystemDirectories">Есть ли файл с таким именем в папках Windows.</param>
    public static IReadOnlyList<string> Validate(
        LimitRule rule,
        ProtectionRules protection,
        Func<string, string?> knownPath,
        Func<string, bool> existsInSystemDirectories)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 80)
            errors.Add("Название должно быть от 1 до 80 символов");
        if (rule.Matchers.Count == 0)
            errors.Add("Добавьте хотя бы одну программу");
        if (rule.Matchers.Count > 50)
            errors.Add("Слишком много программ в одной группе (максимум 50)");
        if (rule.WeekdayMinutes is < 0 or > LimitRule.MaxMinutes)
            errors.Add("Лимит в будни должен быть от 0 до 1440 минут");
        if (rule.WeekendMinutes is < 0 or > LimitRule.MaxMinutes)
            errors.Add("Лимит в выходные должен быть от 0 до 1440 минут");
        if (rule.WeekdayWindow is { IsEmpty: true } || rule.EffectiveWeekendWindow is { IsEmpty: true })
            errors.Add("Начало и конец разрешённого времени должны различаться");

        foreach (var matcher in rule.Matchers)
        {
            if (matcher.Type != Matcher.ExeType)
            {
                errors.Add($"Тип правила «{matcher.Type}» пока не поддерживается");
                continue;
            }

            var reason = protection.WhyCannotLimit(matcher.Exe, knownPath(matcher.Exe), existsInSystemDirectories);
            if (reason is not null)
                errors.Add($"{matcher.Exe}: {reason}");
        }

        return errors;
    }

    public static IReadOnlyList<string> Validate(AccountingSettings settings)
    {
        var errors = new List<string>();
        if (settings.IdleThresholdSeconds is < MinIdleThresholdSeconds or > MaxIdleThresholdSeconds)
            errors.Add("Порог бездействия должен быть от 1 до 60 минут");
        if (settings.DayResetTime.Second != 0 || settings.DayResetTime.Millisecond != 0)
            errors.Add("Время сброса задаётся с точностью до минуты");
        return errors;
    }
}
