using System.Collections.Concurrent;
using Wellbeing.Core;
using Wellbeing.Core.Abstractions;
using Wellbeing.Service.Data;

namespace Wellbeing.Service.Tracking;

public sealed record AppInfo(string Exe, string DisplayName, string? Path, bool HasIcon, long FirstSeenUtc, long LastSeenUtc);

/// <summary>Известные программы: имя, путь, иконка. Новые регистрируются по heartbeat-ам агента.</summary>
public sealed class AppCatalog
{
    readonly AppRepository _repository;
    readonly IAppMetadataReader _metadata;
    readonly IClock _clock;
    readonly ILogger<AppCatalog> _log;
    readonly ConcurrentDictionary<string, AppInfo> _apps = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, long> _dirtyLastSeen = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public AppCatalog(AppRepository repository, IAppMetadataReader metadata, IClock clock, ILogger<AppCatalog> log)
    {
        _repository = repository;
        _metadata = metadata;
        _clock = clock;
        _log = log;
        foreach (var row in repository.LoadAll())
            _apps[row.Exe] = new AppInfo(row.Exe, row.DisplayName, row.Path, row.HasIcon != 0, row.FirstSeenUtc, row.LastSeenUtc);
    }

    public IReadOnlyCollection<AppInfo> All => _apps.Values.ToList();

    public AppInfo? Get(string exe) => _apps.GetValueOrDefault(exe);

    public string DisplayName(string exe) => _apps.TryGetValue(exe, out var app) ? app.DisplayName : exe;

    public byte[]? Icon(string exe) => _repository.GetIcon(exe);

    /// <summary>Программа замечена активной. Метаданные новой программы читаются в фоне.</summary>
    public void Touch(string exe, string? path)
    {
        var now = _clock.UtcNow.ToUnixTimeSeconds();
        if (_apps.TryGetValue(exe, out var known))
        {
            _apps[exe] = known with { LastSeenUtc = now };
            _dirtyLastSeen[exe] = now;
            var pathChanged = path is not null && !string.Equals(known.Path, path, StringComparison.OrdinalIgnoreCase);
            if (!pathChanged) return;
        }

        if (!_inFlight.TryAdd(exe, 0)) return;
        _ = Task.Run(() =>
        {
            try
            {
                Register(exe, path);
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "Не удалось прочитать данные программы {Exe}", exe);
            }
            finally
            {
                _inFlight.TryRemove(exe, out _);
            }
        });
    }

    /// <summary>Регистрирует программу, выбранную пользователем через диалог.</summary>
    public AppInfo Register(string path) => Register(ExeName.Normalize(path), path);

    AppInfo Register(string exe, string? path)
    {
        var now = _clock.UtcNow.ToUnixTimeSeconds();
        var metadata = path is not null && File.Exists(path)
            ? _metadata.Read(path)
            : new AppMetadata(Path.GetFileNameWithoutExtension(exe), null);
        _repository.Upsert(exe, metadata.DisplayName, path, metadata.IconPng, now);

        var previous = _apps.GetValueOrDefault(exe);
        var info = new AppInfo(exe, metadata.DisplayName, path ?? previous?.Path,
            metadata.IconPng is not null || previous?.HasIcon == true, previous?.FirstSeenUtc ?? now, now);
        _apps[exe] = info;
        return info;
    }

    public void FlushLastSeen()
    {
        var items = _dirtyLastSeen.ToArray();
        foreach (var (exe, _) in items) _dirtyLastSeen.TryRemove(exe, out _);
        _repository.TouchLastSeen(items);
    }
}
