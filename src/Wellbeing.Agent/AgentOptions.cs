using System.Text.Json;
using Wellbeing.Core.Contracts;

namespace Wellbeing.Agent;

/// <param name="ConnectionFile">agent.json с адресом службы и токеном.</param>
/// <param name="Dev">Режим разработки: служба запущена из консоли, в окне доступны DevTools.</param>
sealed record AgentOptions(string ConnectionFile, string DataDirectory, bool Dev)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string LogFile => Path.Combine(DataDirectory, "agent.log");
    public string WebViewDataFolder => Path.Combine(DataDirectory, "WebView2");

    /// <summary>
    /// По умолчанию — установленная служба (%ProgramData%\Wellbeing\agent.json).
    /// <c>--dev</c> — служба, запущенная из консоли (%LOCALAPPDATA%\Wellbeing.Dev\agent.json).
    /// <c>--connection путь</c> — явный файл подключения.
    /// </summary>
    public static AgentOptions FromArgs(string[] args)
    {
        var dev = args.Contains("--dev");
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var index = Array.IndexOf(args, "--connection");
        var connection = index >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : dev
                ? Path.Combine(localData, "Wellbeing.Dev", "agent.json")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wellbeing", "agent.json");

        var dataDirectory = Path.Combine(localData, dev ? "Wellbeing.Dev" : "Wellbeing");
        Directory.CreateDirectory(dataDirectory);
        return new AgentOptions(connection, dataDirectory, dev);
    }

    public AgentConnectionInfo? TryReadConnection()
    {
        try
        {
            var info = JsonSerializer.Deserialize<AgentConnectionInfo>(File.ReadAllText(ConnectionFile), Json);
            return info is { BaseUrl.Length: > 0, Token.Length: > 0 } ? info : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
