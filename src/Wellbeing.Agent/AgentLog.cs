namespace Wellbeing.Agent;

/// <summary>Простой файловый лог агента (%LOCALAPPDATA%\Wellbeing\agent.log, до ~1 МБ).</summary>
sealed class AgentLog(string path)
{
    const long MaxSize = 1024 * 1024;
    readonly Lock _gate = new();

    public void Info(string message) => Write("INF", message, null);

    public void Error(string message, Exception? error) => Write("ERR", message, error);

    void Write(string level, string message, Exception? error)
    {
        lock (_gate)
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxSize)
                    File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{(error is null ? "" : Environment.NewLine + error)}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
