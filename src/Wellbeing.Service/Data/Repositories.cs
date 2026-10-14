using System.Text.Json;
using Dapper;
using Wellbeing.Core.Policies;
using Wellbeing.Core.Time;
using Wellbeing.Core.Tracking;

namespace Wellbeing.Service.Data;

public sealed class UsageRepository(Database db)
{
    public void Add(IReadOnlyList<MinuteUsage> items)
    {
        if (items.Count == 0) return;
        using var connection = db.Open();
        using var tx = connection.BeginTransaction();
        connection.Execute(
            """
            INSERT INTO usage_minute(day, minute_utc, exe, seconds) VALUES (@Day, @MinuteUtc, @Exe, @Seconds)
            ON CONFLICT(day, minute_utc, exe) DO UPDATE SET seconds = seconds + excluded.seconds;
            """,
            items.Select(i => new { Day = LogicalDay.Format(i.Day), i.MinuteUtc, i.Exe, i.Seconds }),
            tx);
        tx.Commit();
    }

    public Dictionary<string, long> TotalsForDay(DateOnly day) => TotalsBetween(day, day);

    public Dictionary<string, long> TotalsBetween(DateOnly from, DateOnly to)
    {
        using var connection = db.Open();
        return connection
            .Query<(string Exe, long Seconds)>(
                "SELECT exe, SUM(seconds) FROM usage_minute WHERE day BETWEEN @from AND @to GROUP BY exe;",
                new { from = LogicalDay.Format(from), to = LogicalDay.Format(to) })
            .ToDictionary(r => r.Exe, r => r.Seconds, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<(long MinuteUtc, string Exe, long Seconds)> Timeline(DateOnly day)
    {
        using var connection = db.Open();
        return connection
            .Query<(long, string, long)>(
                "SELECT minute_utc, exe, seconds FROM usage_minute WHERE day = @day ORDER BY minute_utc;",
                new { day = LogicalDay.Format(day) })
            .ToList();
    }

    public IReadOnlyList<(string Day, string Exe, long Seconds)> Daily(DateOnly from, DateOnly to)
    {
        using var connection = db.Open();
        return connection
            .Query<(string, string, long)>(
                "SELECT day, exe, SUM(seconds) FROM usage_minute WHERE day BETWEEN @from AND @to GROUP BY day, exe ORDER BY day;",
                new { from = LogicalDay.Format(from), to = LogicalDay.Format(to) })
            .ToList();
    }
}

public sealed class AppRow
{
    public string Exe { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Path { get; set; }
    public long HasIcon { get; set; }
    public long FirstSeenUtc { get; set; }
    public long LastSeenUtc { get; set; }
}

public sealed class AppRepository(Database db)
{
    public IReadOnlyList<AppRow> LoadAll()
    {
        using var connection = db.Open();
        return connection.Query<AppRow>(
            """
            SELECT exe AS Exe, display_name AS DisplayName, path AS Path, (icon_png IS NOT NULL) AS HasIcon,
                   first_seen_utc AS FirstSeenUtc, last_seen_utc AS LastSeenUtc
            FROM apps;
            """).ToList();
    }

    public void Upsert(string exe, string displayName, string? path, byte[]? icon, long nowUtc)
    {
        using var connection = db.Open();
        connection.Execute(
            """
            INSERT INTO apps(exe, display_name, path, icon_png, first_seen_utc, last_seen_utc)
            VALUES (@exe, @displayName, @path, @icon, @now, @now)
            ON CONFLICT(exe) DO UPDATE SET
                display_name = excluded.display_name,
                path = COALESCE(excluded.path, apps.path),
                icon_png = COALESCE(excluded.icon_png, apps.icon_png),
                last_seen_utc = excluded.last_seen_utc;
            """,
            new { exe, displayName, path, icon, now = nowUtc });
    }

    public void TouchLastSeen(IReadOnlyCollection<KeyValuePair<string, long>> items)
    {
        if (items.Count == 0) return;
        using var connection = db.Open();
        using var tx = connection.BeginTransaction();
        connection.Execute(
            "UPDATE apps SET last_seen_utc = @Value WHERE exe = @Key AND last_seen_utc < @Value;",
            items.Select(i => new { i.Key, i.Value }),
            tx);
        tx.Commit();
    }

    public byte[]? GetIcon(string exe)
    {
        using var connection = db.Open();
        return connection.ExecuteScalar<byte[]?>("SELECT icon_png FROM apps WHERE exe = @exe;", new { exe });
    }
}

public sealed class PolicyRepository(Database db)
{
    public IReadOnlyList<PolicyRevision> LoadAll()
    {
        using var connection = db.Open();
        return connection
            .Query<(long Seq, long CreatedUtc, string EffectiveDay, string Json, string? Summary)>(
                "SELECT seq, created_utc, effective_day, policy_json, summary FROM policy_revision ORDER BY seq;")
            .Select(r => new PolicyRevision(
                r.Seq,
                LogicalDay.Parse(r.EffectiveDay),
                PolicyJson.Deserialize(r.Json),
                DateTimeOffset.FromUnixTimeSeconds(r.CreatedUtc).UtcDateTime,
                r.Summary))
            .ToList();
    }

    public void Insert(IReadOnlyList<PolicyRevision> revisions)
    {
        using var connection = db.Open();
        using var tx = connection.BeginTransaction();
        connection.Execute(
            "INSERT INTO policy_revision(seq, created_utc, effective_day, policy_json, summary) VALUES (@Seq, @CreatedUtc, @Day, @Json, @Summary);",
            revisions.Select(r => new
            {
                r.Seq,
                CreatedUtc = new DateTimeOffset(r.CreatedUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
                Day = LogicalDay.Format(r.EffectiveDay),
                Json = PolicyJson.Serialize(r.Policy),
                r.Summary,
            }),
            tx);
        tx.Commit();
    }
}

public sealed record EventLogEntry(long Id, DateTimeOffset Timestamp, string Kind, string Message);

public sealed class StateRepository(Database db)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string? Get(string key)
    {
        using var connection = db.Open();
        return connection.ExecuteScalar<string?>("SELECT value FROM kv_state WHERE key = @key;", new { key });
    }

    public void Set(string key, string value)
    {
        using var connection = db.Open();
        connection.Execute(
            "INSERT INTO kv_state(key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            new { key, value });
    }

    public HashSet<(Guid LimitId, int Threshold)> WarningsSent(DateOnly day)
    {
        using var connection = db.Open();
        return connection
            .Query<(string LimitId, long Threshold)>(
                "SELECT limit_id, threshold_min FROM warning_sent WHERE day = @day;",
                new { day = LogicalDay.Format(day) })
            .Select(r => (Guid.Parse(r.LimitId), (int)r.Threshold))
            .ToHashSet();
    }

    public void MarkWarningSent(DateOnly day, Guid limitId, int threshold)
    {
        using var connection = db.Open();
        connection.Execute(
            "INSERT OR IGNORE INTO warning_sent(day, limit_id, threshold_min) VALUES (@day, @limitId, @threshold);",
            new { day = LogicalDay.Format(day), limitId = limitId.ToString(), threshold });
    }

    public T? GetSetting<T>(string key)
    {
        using var connection = db.Open();
        var json = connection.ExecuteScalar<string?>("SELECT value_json FROM settings WHERE key = @key;", new { key });
        return json is null ? default : JsonSerializer.Deserialize<T>(json, Json);
    }

    public void SetSetting<T>(string key, T value)
    {
        using var connection = db.Open();
        connection.Execute(
            "INSERT INTO settings(key, value_json) VALUES (@key, @json) ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json;",
            new { key, json = JsonSerializer.Serialize(value, Json) });
    }

    public void Log(string kind, string message)
    {
        using var connection = db.Open();
        connection.Execute(
            "INSERT INTO event_log(ts_utc, kind, message) VALUES (@ts, @kind, @message);",
            new { ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), kind, message });
    }

    public IReadOnlyList<EventLogEntry> RecentEvents(int take)
    {
        using var connection = db.Open();
        return connection
            .Query<(long Id, long Ts, string Kind, string Message)>(
                "SELECT id, ts_utc, kind, message FROM event_log ORDER BY id DESC LIMIT @take;",
                new { take })
            .Select(r => new EventLogEntry(r.Id, DateTimeOffset.FromUnixTimeSeconds(r.Ts), r.Kind, r.Message))
            .ToList();
    }

    public void PruneEvents(TimeSpan olderThan)
    {
        using var connection = db.Open();
        connection.Execute(
            "DELETE FROM event_log WHERE ts_utc < @cutoff;",
            new { cutoff = DateTimeOffset.UtcNow.Subtract(olderThan).ToUnixTimeSeconds() });
    }
}
