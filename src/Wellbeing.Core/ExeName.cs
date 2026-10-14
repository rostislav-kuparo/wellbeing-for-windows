namespace Wellbeing.Core;

/// <summary>Имена исполняемых файлов храним без пути и в нижнем регистре: <c>game.exe</c>.</summary>
public static class ExeName
{
    static readonly char[] InvalidChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0'];

    public static string Normalize(string exeOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exeOrPath);
        var trimmed = exeOrPath.Trim().TrimEnd('\\', '/');
        var slash = trimmed.LastIndexOfAny(['\\', '/']);
        return (slash >= 0 ? trimmed[(slash + 1)..] : trimmed).ToLowerInvariant();
    }

    public static bool IsValid(string? exe) =>
        !string.IsNullOrWhiteSpace(exe)
        && exe.Length is > 4 and <= 260
        && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && exe.IndexOfAny(InvalidChars) < 0;
}
