using System.Diagnostics;

namespace Wellbeing.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Монотонное время: не прыгает при переводе часов.</summary>
    TimeSpan Monotonic { get; }

    DateTime ToLocal(DateTimeOffset utc);

    DateTimeOffset FromLocal(DateTime local);
}

public sealed class SystemClock : IClock
{
    readonly long _start = Stopwatch.GetTimestamp();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeSpan Monotonic => Stopwatch.GetElapsedTime(_start);
    public DateTime ToLocal(DateTimeOffset utc) => DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local).DateTime, DateTimeKind.Unspecified);

    public DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
    }
}

public sealed record ProcessEntry(int Pid, int SessionId, string Exe, string? Path);

public interface IProcessSource
{
    /// <param name="exeFilter">Если задан, путь запрашивается только для подходящих по имени процессов.</param>
    IReadOnlyList<ProcessEntry> GetProcesses(Func<string, bool>? exeFilter = null);

    ProcessEntry? TryGet(int pid);
}

public interface IProcessTerminator
{
    bool TryTerminate(int pid);
}

/// <summary>Мгновенные уведомления о старте процессов (WMI Win32_ProcessStartTrace).</summary>
public interface IProcessStartWatcher : IDisposable
{
    /// <summary>pid, sessionId, имя процесса.</summary>
    event Action<int, int, string>? ProcessStarted;

    void Start();
}

public sealed record SessionState(int SessionId, string? UserName, bool IsLocked)
{
    public bool HasUser => !string.IsNullOrEmpty(UserName);
}

public interface ISessionInfo
{
    /// <summary>Сессия, которая сейчас на физическом экране; −1, если такой нет.</summary>
    int ActiveConsoleSessionId { get; }

    SessionState GetState(int sessionId);
}

public interface IUserProcessLauncher
{
    /// <summary>Запускает процесс от имени пользователя сессии. Возвращает PID; при ошибке бросает исключение.</summary>
    int LaunchInSession(int sessionId, string exePath, string? arguments);
}

public interface IUserMessenger
{
    /// <summary>Системное окно сообщения в сессии пользователя — когда агента нет.</summary>
    void Show(int sessionId, string title, string message);
}

public sealed record AppMetadata(string DisplayName, byte[]? IconPng);

public interface IAppMetadataReader
{
    AppMetadata Read(string exePath);
}

public sealed record ForegroundWindow(int ProcessId, string Exe, string? Path, string? Title, bool IsFullscreen);

/// <summary>Активное окно и бездействие в сессии пользователя (используется агентом).</summary>
public interface IForegroundProbe
{
    ForegroundWindow? GetForeground();

    int GetIdleSeconds();
}
