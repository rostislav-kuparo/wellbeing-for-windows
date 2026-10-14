using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Wellbeing.Core;
using Wellbeing.Core.Abstractions;

namespace Wellbeing.Win32;

/// <summary>Активное окно и бездействие пользователя. Работает в сессии пользователя (в агенте).</summary>
public sealed class ForegroundProbe : IForegroundProbe
{
    public ForegroundWindow? GetForeground()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        var path = ProcessPaths.TryGetPath(pid);
        var exe = path is null ? null : ExeName.Normalize(path);

        // UWP-приложения рисуются внутри ApplicationFrameHost.exe — ищем настоящий процесс среди дочерних окон.
        if (exe == "applicationframehost.exe" && FindHostedProcess(hwnd, pid) is { } hostedPid)
        {
            pid = hostedPid;
            path = ProcessPaths.TryGetPath(pid);
            exe = path is null ? null : ExeName.Normalize(path);
        }

        exe ??= TryGetProcessName(pid);
        if (exe is null) return null;

        return new ForegroundWindow(pid, exe, path, GetTitle(hwnd), IsFullscreen(hwnd));
    }

    public int GetIdleSeconds()
    {
        var info = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Native.LASTINPUTINFO>() };
        if (!Native.GetLastInputInfo(ref info)) return 0;
        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return (int)(idleMs / 1000);
    }

    static int? FindHostedProcess(IntPtr frame, int framePid)
    {
        int? found = null;
        Native.EnumChildWindows(frame, (child, _) =>
        {
            Native.GetWindowThreadProcessId(child, out var childPid);
            if (childPid == 0 || childPid == framePid) return true;
            found = childPid;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    static string? TryGetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return (process.ProcessName + ".exe").ToLowerInvariant();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    static string? GetTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        var length = Native.GetWindowText(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : null;
    }

    static string GetClass(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        var length = Native.GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    /// <summary>
    /// Полноэкранное окно (видео, игры): либо Windows сообщает о D3D/презентационном режиме,
    /// либо окно целиком покрывает монитор и при этом не просто развёрнуто.
    /// </summary>
    static bool IsFullscreen(IntPtr hwnd)
    {
        const int qunsBusy = 2, qunsD3DFullScreen = 3, qunsPresentationMode = 4;
        if (Native.SHQueryUserNotificationState(out var state) == 0
            && state is qunsBusy or qunsD3DFullScreen or qunsPresentationMode)
            return true;

        if (hwnd == Native.GetShellWindow() || hwnd == Native.GetDesktopWindow()) return false;
        if (GetClass(hwnd) is "Progman" or "WorkerW") return false;
        if (Native.IsZoomed(hwnd)) return false;
        if (!Native.GetWindowRect(hwnd, out var rect)) return false;

        var monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return false;

        var m = info.rcMonitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }
}

public static class WindowChrome
{
    /// <summary>Тёмный заголовок окна (Windows 10 20H1+ / Windows 11).</summary>
    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        const int dwmwaUseImmersiveDarkMode = 20;
        var value = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, dwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    /// <summary>Разрешает другому процессу (уже запущенному агенту) вывести своё окно на передний план.</summary>
    public static void AllowAnyProcessToTakeForeground()
    {
        const int asfwAny = -1;
        Native.AllowSetForegroundWindow(asfwAny);
    }
}
