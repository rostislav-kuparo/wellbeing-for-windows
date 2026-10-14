using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wellbeing.Core.Contracts;
using Wellbeing.Win32;

namespace Wellbeing.Service.Api;

/// <summary>Токен API. Хранится в agent.json, который могут читать только SYSTEM, администраторы и вошедшие пользователи.</summary>
public sealed class ApiToken
{
    public const string CookieName = "wb_token";

    readonly byte[] _token;

    ApiToken(string token)
    {
        Value = token;
        _token = Encoding.UTF8.GetBytes(token);
    }

    public string Value { get; }

    public bool Matches(string? candidate) =>
        candidate is not null && CryptographicOperations.FixedTimeEquals(_token, Encoding.UTF8.GetBytes(candidate));

    /// <summary>Читает токен из agent.json или создаёт новый (первый запуск после установки).</summary>
    public static ApiToken LoadOrCreate(ServiceOptions options, ILogger log)
    {
        var path = options.ConnectionFilePath;
        AgentConnectionInfo? existing = null;
        try
        {
            if (File.Exists(path))
                existing = JsonSerializer.Deserialize<AgentConnectionInfo>(File.ReadAllText(path), EventBroadcaster.Json);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "Не удалось прочитать {Path}, будет создан новый токен", path);
        }

        var token = existing?.Token is { Length: >= 32 } t ? t : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (existing is null || existing.Token != token || existing.BaseUrl != options.BaseUrl)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new AgentConnectionInfo(options.BaseUrl, token), EventBroadcaster.Json));
            log.LogInformation("Записан файл подключения агента {Path}", path);
        }

        // Права ставим при каждом старте: установщик сбрасывает содержимое папки данных на наследование.
        if (options.IsWindowsService)
            FileAcl.MakeReadableByInteractiveUsers(path);

        return new ApiToken(token);
    }
}

/// <summary>
/// Пускает только запросы на 127.0.0.1/localhost нашего порта (защита от DNS rebinding) и требует токен
/// для всего /api, кроме /api/health. Токен — в заголовке Authorization: Bearer или в cookie wb_token.
/// </summary>
public sealed class ApiSecurityMiddleware(RequestDelegate next, ApiToken token, ServiceOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!IsLocal(request.Host.Host) || request.Host.Port != options.Port)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (request.Path.StartsWithSegments("/api") && !request.Path.Equals("/api/health"))
        {
            var origin = request.Headers.Origin.ToString();
            if (origin.Length > 0 && !IsAllowedOrigin(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!token.Matches(ExtractToken(request)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await next(context);
    }

    static bool IsLocal(string host) => host is "127.0.0.1" or "localhost";

    bool IsAllowedOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && IsLocal(uri.Host) && uri.Port == options.Port;

    static string? ExtractToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();
        return request.Cookies.TryGetValue(ApiToken.CookieName, out var cookie) ? cookie : null;
    }
}
