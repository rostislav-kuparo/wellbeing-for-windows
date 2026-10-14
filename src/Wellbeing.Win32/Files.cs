using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.AccessControl;
using System.Security.Principal;
using Wellbeing.Core.Abstractions;

namespace Wellbeing.Win32;

public sealed class AppMetadataReader : IAppMetadataReader
{
    public AppMetadata Read(string exePath)
    {
        var displayName = Path.GetFileNameWithoutExtension(exePath);
        try
        {
            var version = FileVersionInfo.GetVersionInfo(exePath);
            // ProductName у компонентов Windows — «Microsoft® Windows® Operating System», это не имя программы.
            var described = new[] { version.FileDescription, version.ProductName }
                .Select(s => s?.Trim())
                .FirstOrDefault(s => !string.IsNullOrEmpty(s) && !s.Contains("Microsoft® Windows®", StringComparison.OrdinalIgnoreCase));
            if (described is not null) displayName = described;
        }
        catch (Exception e) when (e is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
        }

        return new AppMetadata(displayName, TryReadIcon(exePath));
    }

    static byte[]? TryReadIcon(string exePath)
    {
        try
        {
            using var icon = Icon.ExtractIcon(exePath, 0, 64) ?? Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public static class WindowsPaths
{
    static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static string Windows => WindowsDirectory;

    /// <summary>Есть ли exe с таким именем среди программ Windows (тогда его нельзя ограничивать).</summary>
    public static bool ExistsInSystemDirectories(string exe) =>
        File.Exists(Path.Combine(WindowsDirectory, exe))
        || File.Exists(Path.Combine(WindowsDirectory, "System32", exe))
        || File.Exists(Path.Combine(WindowsDirectory, "SysWOW64", exe));

    /// <summary>Папка рантайма WebView2 (на ней работает окно Wellbeing и многие другие программы).</summary>
    public static string EdgeWebViewDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView");
}

public static class FileAcl
{
    static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly SecurityIdentifier Interactive = new(WellKnownSidType.InteractiveSid, null);

    /// <summary>
    /// Права файла: читать могут только SYSTEM, администраторы и интерактивно вошедшие пользователи.
    /// Используется для agent.json с токеном API.
    /// </summary>
    public static void MakeReadableByInteractiveUsers(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Interactive, FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    /// <summary>Папка данных: только SYSTEM и администраторы, без наследования прав от ProgramData.</summary>
    public static void RestrictDirectoryToSystemAndAdmins(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    public static bool IsRunningAsSystem()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;
    }
}
