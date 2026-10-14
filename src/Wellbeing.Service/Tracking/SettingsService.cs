using Wellbeing.Service.Data;

namespace Wellbeing.Service.Tracking;

public sealed record NotificationSettings(bool WarningsEnabled, IReadOnlyList<int> WarnMinutes)
{
    public static NotificationSettings Default { get; } = new(true, [10, 5, 1]);
    public static IReadOnlyList<int> AllowedMinutes { get; } = [30, 15, 10, 5, 3, 1];
}

/// <summary>Настройки, которые не влияют на учёт и поэтому применяются сразу.</summary>
public sealed class SettingsService(StateRepository state)
{
    const string NotificationsKey = "notifications";

    NotificationSettings? _notifications;

    public NotificationSettings Notifications =>
        _notifications ??= state.GetSetting<NotificationSettings>(NotificationsKey) ?? NotificationSettings.Default;

    public void Save(NotificationSettings settings)
    {
        var normalized = settings with
        {
            WarnMinutes = settings.WarnMinutes
                .Where(NotificationSettings.AllowedMinutes.Contains)
                .Distinct()
                .OrderDescending()
                .ToList(),
        };
        state.SetSetting(NotificationsKey, normalized);
        _notifications = normalized;
    }
}
