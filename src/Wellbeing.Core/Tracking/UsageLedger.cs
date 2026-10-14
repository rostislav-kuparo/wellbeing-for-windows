namespace Wellbeing.Core.Tracking;

/// <summary>Сколько секунд программа была активна в данной минуте (агрегат для БД).</summary>
public sealed record MinuteUsage(DateOnly Day, long MinuteUtc, string Exe, int Seconds);

/// <summary>
/// Учёт в памяти: итоги текущего дня по программам и ещё не записанные в БД поминутные приращения.
/// Потокобезопасен: пишет фоновый тик, читают API и сброс в БД.
/// </summary>
public sealed class UsageLedger
{
    readonly Lock _gate = new();
    readonly Dictionary<string, long> _today = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(DateOnly Day, long Minute, string Exe), int> _pending = new();
    DateOnly _day;

    public DateOnly Day
    {
        get { lock (_gate) return _day; }
    }

    /// <summary>Начинает новый день; <paramref name="persistedTotals"/> — то, что уже записано в БД за этот день.</summary>
    public void StartDay(DateOnly day, IReadOnlyDictionary<string, long> persistedTotals)
    {
        lock (_gate)
        {
            _day = day;
            _today.Clear();
            foreach (var (exe, seconds) in persistedTotals)
                _today[exe] = seconds;
            foreach (var ((pendingDay, _, exe), seconds) in _pending)
                if (pendingDay == day)
                    _today[exe] = _today.GetValueOrDefault(exe) + seconds;
        }
    }

    public void Credit(DateOnly day, DateTimeOffset nowUtc, IReadOnlyList<string> exes, int seconds)
    {
        if (seconds <= 0 || exes.Count == 0) return;
        var minute = nowUtc.ToUnixTimeSeconds() / 60 * 60;
        lock (_gate)
        {
            foreach (var exe in exes)
            {
                var key = (day, minute, exe);
                _pending[key] = _pending.GetValueOrDefault(key) + seconds;
                if (day == _day)
                    _today[exe] = _today.GetValueOrDefault(exe) + seconds;
            }
        }
    }

    public IReadOnlyDictionary<string, long> SnapshotToday()
    {
        lock (_gate) return new Dictionary<string, long>(_today, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<MinuteUsage> DrainPending()
    {
        lock (_gate)
        {
            var result = _pending.Select(p => new MinuteUsage(p.Key.Day, p.Key.Minute, p.Key.Exe, p.Value)).ToList();
            _pending.Clear();
            return result;
        }
    }

    /// <summary>Возвращает приращения обратно, если запись в БД не удалась.</summary>
    public void Restore(IEnumerable<MinuteUsage> items)
    {
        lock (_gate)
        {
            foreach (var item in items)
            {
                var key = (item.Day, item.MinuteUtc, item.Exe);
                _pending[key] = _pending.GetValueOrDefault(key) + item.Seconds;
            }
        }
    }
}
