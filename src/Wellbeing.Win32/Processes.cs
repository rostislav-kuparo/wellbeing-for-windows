using System.Diagnostics;
using System.Management;
using System.Text;
using Wellbeing.Core;
using Wellbeing.Core.Abstractions;

namespace Wellbeing.Win32;

public static class ProcessPaths
{
    public static string? TryGetPath(int pid)
    {
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var size = 1024;
            var buffer = new StringBuilder(size);
            return Native.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }
}

public sealed class ProcessSource : IProcessSource
{
    public IReadOnlyList<ProcessEntry> GetProcesses(Func<string, bool>? exeFilter = null)
    {
        var processes = Process.GetProcesses();
        var result = new List<ProcessEntry>(exeFilter is null ? processes.Length : 8);
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.Id is 0 or 4) continue;
                    var exe = (process.ProcessName + ".exe").ToLowerInvariant();
                    if (exeFilter is not null && !exeFilter(exe)) continue;

                    var path = ProcessPaths.TryGetPath(process.Id);
                    if (path is not null) exe = ExeName.Normalize(path);
                    result.Add(new ProcessEntry(process.Id, process.SessionId, exe, path));
                }
                catch (InvalidOperationException)
                {
                    // процесс уже завершился
                }
            }
        }

        return result;
    }

    public ProcessEntry? TryGet(int pid)
    {
        if (!Native.ProcessIdToSessionId(pid, out var session)) return null;
        var path = ProcessPaths.TryGetPath(pid);
        if (path is not null) return new ProcessEntry(pid, session, ExeName.Normalize(path), path);
        try
        {
            using var process = Process.GetProcessById(pid);
            return new ProcessEntry(pid, session, (process.ProcessName + ".exe").ToLowerInvariant(), null);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

public sealed class ProcessTerminator : IProcessTerminator
{
    public bool TryTerminate(int pid)
    {
        var handle = Native.OpenProcess(Native.PROCESS_TERMINATE, false, pid);
        if (handle == IntPtr.Zero) return false;
        try
        {
            return Native.TerminateProcess(handle, 1);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }
}

/// <summary>
/// Старт процессов через WMI-событие Win32_ProcessStartTrace (ядро присылает его сразу, без опроса).
/// Требует прав администратора — служба работает от SYSTEM.
/// </summary>
public sealed class WmiProcessStartWatcher : IProcessStartWatcher
{
    ManagementEventWatcher? _watcher;

    public event Action<int, int, string>? ProcessStarted;

    public void Start()
    {
        var watcher = new ManagementEventWatcher(
            new ManagementScope(@"\\.\root\CIMV2"),
            new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
        watcher.EventArrived += OnEventArrived;
        try
        {
            watcher.Start();
        }
        catch
        {
            watcher.Dispose();
            throw;
        }

        _watcher = watcher;
    }

    void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            using var ev = e.NewEvent;
            var pid = Convert.ToInt32(ev["ProcessID"]);
            var session = Convert.ToInt32(ev["SessionID"]);
            var name = ev["ProcessName"] as string ?? string.Empty;
            ProcessStarted?.Invoke(pid, session, name);
        }
        catch (Exception)
        {
            // Событие не должно ронять WMI-поток; пропущенный процесс поймает периодическая проверка.
        }
    }

    public void Dispose()
    {
        if (_watcher is null) return;
        try
        {
            _watcher.Stop();
        }
        catch (ManagementException)
        {
        }

        _watcher.Dispose();
        _watcher = null;
    }
}
