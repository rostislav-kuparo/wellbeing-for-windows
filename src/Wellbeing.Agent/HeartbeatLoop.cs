using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Wellbeing.Core.Abstractions;
using Wellbeing.Core.Contracts;

namespace Wellbeing.Agent;

/// <summary>Раз в секунду отправляет службе активное окно и время бездействия.</summary>
sealed class HeartbeatLoop : IDisposable
{
    readonly AgentOptions _options;
    readonly IForegroundProbe _probe;
    readonly Func<bool> _isLocked;
    readonly Action<HeartbeatResponse> _onResponse;
    readonly AgentLog _log;
    readonly CancellationTokenSource _stop = new();
    readonly int _sessionId;
    readonly int _processId;
    readonly string _version;
    HttpClient? _client;
    int _failures;

    public HeartbeatLoop(AgentOptions options, IForegroundProbe probe, Func<bool> isLocked, Action<HeartbeatResponse> onResponse, AgentLog log)
    {
        _options = options;
        _probe = probe;
        _isLocked = isLocked;
        _onResponse = onResponse;
        _log = log;
        using var self = Process.GetCurrentProcess();
        _sessionId = self.SessionId;
        _processId = self.Id;
        _version = typeof(HeartbeatLoop).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    /// <summary>Есть ли связь со службой (последний heartbeat прошёл успешно).</summary>
    public bool Connected => _failures == 0 && _client is not null;

    public void Start() => _ = Task.Run(() => RunAsync(_stop.Token));

    async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                await SendAsync(ct);
                if (_failures > 0) _log.Info("Связь со службой восстановлена");
                _failures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                if (_failures++ % 60 == 0) _log.Error("Служба недоступна", e);
                // Служба могла перезапуститься с новым портом или токеном — перечитаем agent.json.
                if (_failures % 5 == 0) ResetClient();
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    async Task SendAsync(CancellationToken ct)
    {
        var client = _client ??= CreateClient() ?? throw new InvalidOperationException($"Нет файла подключения {_options.ConnectionFile}");

        var foreground = _probe.GetForeground();
        var request = new HeartbeatRequest(
            _sessionId,
            _processId,
            foreground?.ProcessId ?? 0,
            foreground?.Exe,
            foreground?.Path,
            foreground?.Title,
            _probe.GetIdleSeconds(),
            foreground?.IsFullscreen ?? false,
            _isLocked(),
            _version);

        using var response = await client.PostAsJsonAsync("api/agent/heartbeat", request, AgentOptions.Json, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            ResetClient();
            throw new HttpRequestException("Служба отклонила токен", null, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<HeartbeatResponse>(AgentOptions.Json, ct);
        if (body is not null) _onResponse(body);
    }

    HttpClient? CreateClient()
    {
        var info = _options.TryReadConnection();
        if (info is null) return null;
        var client = new HttpClient { BaseAddress = new Uri(info.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(3) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        return client;
    }

    void ResetClient()
    {
        _client?.Dispose();
        _client = null;
    }

    public void Dispose()
    {
        _stop.Cancel();
        ResetClient();
    }
}
