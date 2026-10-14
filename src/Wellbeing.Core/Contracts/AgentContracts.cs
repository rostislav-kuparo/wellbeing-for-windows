namespace Wellbeing.Core.Contracts;

/// <summary>Агент → служба, раз в секунду.</summary>
public sealed record HeartbeatRequest(
    int SessionId,
    int AgentProcessId,
    int ForegroundProcessId,
    string? Exe,
    string? ExePath,
    string? WindowTitle,
    int IdleSeconds,
    bool IsFullscreen,
    bool IsLocked,
    string? AgentVersion);

public sealed record AgentNotification(string Title, string Message, bool Important);

public sealed record HeartbeatResponse(IReadOnlyList<AgentNotification> Notifications, string TrayText);

/// <summary>Файл agent.json: как агенту достучаться до службы. Читать его могут только SYSTEM, администраторы и вошедшие пользователи.</summary>
public sealed record AgentConnectionInfo(string BaseUrl, string Token);
