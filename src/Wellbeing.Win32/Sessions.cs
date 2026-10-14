using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Wellbeing.Core.Abstractions;

namespace Wellbeing.Win32;

public sealed class WtsSessionInfo : ISessionInfo
{
    public int ActiveConsoleSessionId
    {
        get
        {
            var id = Native.WTSGetActiveConsoleSessionId();
            return id == 0xFFFFFFFF ? -1 : (int)id;
        }
    }

    public SessionState GetState(int sessionId)
    {
        if (sessionId < 0) return new SessionState(sessionId, null, false);

        string? user = null;
        if (Native.WTSQuerySessionInformation(IntPtr.Zero, sessionId, Native.WtsInfoClass.WTSUserName, out var nameBuffer, out _))
        {
            try
            {
                user = Marshal.PtrToStringUni(nameBuffer);
            }
            finally
            {
                Native.WTSFreeMemory(nameBuffer);
            }
        }

        // WTSINFOEXW: DWORD Level; затем union, выровненный по 8 байт (в нём LARGE_INTEGER).
        // WTSINFOEX_LEVEL1_W: ULONG SessionId; INT SessionState; LONG SessionFlags → смещение флагов 8 + 8 = 16.
        var locked = false;
        if (Native.WTSQuerySessionInformation(IntPtr.Zero, sessionId, Native.WtsInfoClass.WTSSessionInfoEx, out var infoBuffer, out _))
        {
            try
            {
                const int wtsSessionStateLock = 0;
                if (Marshal.ReadInt32(infoBuffer, 0) == 1)
                    locked = Marshal.ReadInt32(infoBuffer, 16) == wtsSessionStateLock;
            }
            finally
            {
                Native.WTSFreeMemory(infoBuffer);
            }
        }

        return new SessionState(sessionId, user, locked);
    }
}

/// <summary>Запуск агента в сессии пользователя из службы (нужны права SYSTEM).</summary>
public sealed class UserProcessLauncher : IUserProcessLauncher
{
    public int LaunchInSession(int sessionId, string exePath, string? arguments)
    {
        if (!Native.WTSQueryUserToken((uint)sessionId, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken");

        var environment = IntPtr.Zero;
        try
        {
            if (!Native.CreateEnvironmentBlock(out environment, token, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateEnvironmentBlock");

            var startup = new Native.STARTUPINFO
            {
                cb = Marshal.SizeOf<Native.STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
            };
            var commandLine = new StringBuilder($"\"{exePath}\"");
            if (!string.IsNullOrEmpty(arguments)) commandLine.Append(' ').Append(arguments);

            if (!Native.CreateProcessAsUser(token, exePath, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    Native.CREATE_UNICODE_ENVIRONMENT | Native.NORMAL_PRIORITY_CLASS, environment,
                    Path.GetDirectoryName(exePath), ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser");

            Native.CloseHandle(info.hThread);
            Native.CloseHandle(info.hProcess);
            return info.dwProcessId;
        }
        finally
        {
            if (environment != IntPtr.Zero) Native.DestroyEnvironmentBlock(environment);
            Native.CloseHandle(token);
        }
    }
}

/// <summary>Системное окно сообщения в сессии пользователя — когда агент не запущен.</summary>
public sealed class WtsMessenger : IUserMessenger
{
    public void Show(int sessionId, string title, string message)
    {
        const uint mbIconWarning = 0x30, mbSetForeground = 0x10000, mbTopMost = 0x40000;
        Native.WTSSendMessage(IntPtr.Zero, sessionId, title, title.Length * 2, message, message.Length * 2,
            mbIconWarning | mbSetForeground | mbTopMost, 60, out _, false);
    }
}
