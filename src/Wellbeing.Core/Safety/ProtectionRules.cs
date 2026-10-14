namespace Wellbeing.Core.Safety;

/// <summary>
/// Белый список: что нельзя ограничить и нельзя завершить ни при каких настройках.
/// Ошибка в лимитах не должна ломать Windows и сам Wellbeing.
/// </summary>
public sealed class ProtectionRules
{
    /// <summary>Критичные процессы Windows и инструменты, которые нужны для восстановления системы.</summary>
    public static IReadOnlySet<string> CriticalExes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "svchost.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "lsass.exe", "lsaiso.exe",
        "services.exe", "smss.exe", "dwm.exe", "fontdrvhost.exe", "sihost.exe", "taskhostw.exe", "ctfmon.exe",
        "runtimebroker.exe", "searchhost.exe", "searchindexer.exe", "startmenuexperiencehost.exe",
        "shellexperiencehost.exe", "shellhost.exe", "textinputhost.exe", "applicationframehost.exe", "lockapp.exe",
        "logonui.exe", "userinit.exe", "consent.exe", "conhost.exe", "dllhost.exe", "audiodg.exe", "spoolsv.exe",
        "wudfhost.exe", "msmpeng.exe", "nissrv.exe", "securityhealthservice.exe", "securityhealthsystray.exe",
        "smartscreen.exe", "taskmgr.exe", "mmc.exe", "regedit.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "windowsterminal.exe", "openconsole.exe", "msiexec.exe", "systemsettings.exe", "msedgewebview2.exe",
        "wellbeing.service.exe", "wellbeing.agent.exe",
    };

    readonly string[] _protectedDirectories;
    readonly HashSet<string> _protectedNames;

    /// <param name="protectedDirectories">Папки, программы из которых не трогаем: Windows, папка установки, WebView2.</param>
    /// <param name="extraProtectedExes">Дополнительные имена, например собственные exe.</param>
    public ProtectionRules(IEnumerable<string> protectedDirectories, IEnumerable<string>? extraProtectedExes = null)
    {
        _protectedDirectories = protectedDirectories
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => NormalizePath(d).TrimEnd('\\') + "\\")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _protectedNames = new HashSet<string>(CriticalExes, StringComparer.OrdinalIgnoreCase);
        foreach (var exe in extraProtectedExes ?? [])
            _protectedNames.Add(ExeName.Normalize(exe));
    }

    public bool IsProtectedName(string exe) => _protectedNames.Contains(exe);

    public bool IsProtectedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = NormalizePath(path);
        foreach (var dir in _protectedDirectories)
            if (normalized.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Можно ли завершить процесс. Процессы сессии 0 (службы) и процессы, путь которых не удалось узнать,
    /// не трогаем никогда.
    /// </summary>
    public bool MayTerminate(int sessionId, string exe, string? path) =>
        sessionId > 0 && path is not null && !IsProtectedPath(path) && !IsProtectedName(exe);

    /// <summary>Почему программу нельзя ограничить; <c>null</c> — можно.</summary>
    /// <param name="existsInSystemDirectories">Есть ли файл с таким именем в папках Windows.</param>
    public string? WhyCannotLimit(string exe, string? knownPath, Func<string, bool> existsInSystemDirectories)
    {
        if (!ExeName.IsValid(exe)) return "Некорректное имя файла: нужен .exe";
        if (IsProtectedName(exe)) return "Системный процесс или часть Wellbeing";
        if (IsProtectedPath(knownPath)) return "Программа из системной папки Windows";
        if (existsInSystemDirectories(exe)) return "Программа с таким именем входит в Windows";
        return null;
    }

    static string NormalizePath(string path)
    {
        var p = path.Trim().Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p[4..];
        try
        {
            return Path.GetFullPath(p);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return p;
        }
    }
}
