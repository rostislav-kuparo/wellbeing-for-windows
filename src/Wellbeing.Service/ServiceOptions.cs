using System.Text.Json;

namespace Wellbeing.Service;

public sealed class ServiceOptions
{
    public const string ServiceName = "WellbeingSvc";
    public const string AgentExe = "wellbeing.agent.exe";
    public const int DefaultPort = 47613;
    public const int DevPort = 47614;

    /// <summary>Работает как служба Windows (иначе — консольный режим для разработки).</summary>
    public required bool IsWindowsService { get; init; }

    public required string DataDirectory { get; init; }
    public required int Port { get; init; }
    public required string InstallDirectory { get; init; }

    /// <summary>Запускать агента в сессии пользователя может только служба от SYSTEM.</summary>
    public bool ManageAgent => IsWindowsService;

    public string DatabasePath => Path.Combine(DataDirectory, "wellbeing.db");
    public string ConnectionFilePath => Path.Combine(DataDirectory, "agent.json");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string AgentPath => Path.Combine(InstallDirectory, "Wellbeing.Agent.exe");
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>
    /// Служба: данные в %ProgramData%\Wellbeing, порт из config.json (по умолчанию 47613).
    /// Консоль: данные в %LOCALAPPDATA%\Wellbeing.Dev, порт 47614. Оба можно переопределить: --data, --port.
    /// </summary>
    public static ServiceOptions FromArgs(string[] args, bool isWindowsService)
    {
        var data = Arg(args, "--data") ?? (isWindowsService
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wellbeing")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wellbeing.Dev"));

        var port = int.TryParse(Arg(args, "--port"), out var p) ? p
            : ReadConfiguredPort(data) ?? (isWindowsService ? DefaultPort : DevPort);

        return new ServiceOptions
        {
            IsWindowsService = isWindowsService,
            DataDirectory = data,
            Port = port,
            InstallDirectory = AppContext.BaseDirectory,
        };
    }

    static string? Arg(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    static int? ReadConfiguredPort(string dataDirectory)
    {
        try
        {
            var path = Path.Combine(dataDirectory, "config.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("port", out var port) && port.TryGetInt32(out var value) ? value : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
