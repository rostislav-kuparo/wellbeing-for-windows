using Microsoft.Data.Sqlite;
using Wellbeing.Core.Policies;
using Wellbeing.Core.Tracking;
using Wellbeing.Service.Data;

namespace Wellbeing.Service.Tests;

public sealed class StorageTests : IDisposable
{
    static readonly DateOnly Day = new(2026, 10, 8);

    readonly string _path = Path.Combine(Path.GetTempPath(), $"wellbeing-test-{Guid.NewGuid():N}.db");
    readonly Database _db;

    public StorageTests()
    {
        _db = new Database(_path);
        _db.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    [Fact]
    public void Migration_is_idempotent()
    {
        _db.Migrate();
        _db.Migrate();
    }

    [Fact]
    public void Usage_increments_accumulate_per_minute()
    {
        var usage = new UsageRepository(_db);
        usage.Add([new MinuteUsage(Day, 600, "game.exe", 20), new MinuteUsage(Day, 660, "game.exe", 60)]);
        usage.Add([new MinuteUsage(Day, 600, "game.exe", 15), new MinuteUsage(Day, 600, "chrome.exe", 5)]);

        var totals = usage.TotalsForDay(Day);
        Assert.Equal(95, totals["game.exe"]);
        Assert.Equal(5, totals["chrome.exe"]);

        var timeline = usage.Timeline(Day);
        Assert.Contains(timeline, t => t is { MinuteUtc: 600, Exe: "game.exe", Seconds: 35 });

        var daily = usage.Daily(Day.AddDays(-1), Day);
        Assert.Equal(2, daily.Count);
        Assert.Empty(usage.TotalsForDay(Day.AddDays(1)));
    }

    [Fact]
    public void Policy_revisions_round_trip()
    {
        var repository = new PolicyRepository(_db);
        var book = new PolicyBook(repository.LoadAll());
        var rule = LimitRule.Create(Guid.NewGuid(), "Игры", ["game.exe", "other.exe"], 60, 120);

        var plan = book.Plan(Day, p => p with { Limits = [rule] });
        var revisions = book.CreateRevisions(plan, Day, DateTime.UtcNow, "test");
        repository.Insert(revisions);
        book.Append(revisions);

        var restored = new PolicyBook(repository.LoadAll());
        Assert.True(PolicyMath.AreIdentical(book.ForDay(Day), restored.ForDay(Day)));
        Assert.Equal(120, restored.ForDay(Day).Find(rule.Id)!.WeekendMinutes);
    }

    [Fact]
    public void Warnings_and_state_persist()
    {
        var state = new StateRepository(_db);
        var id = Guid.NewGuid();
        state.MarkWarningSent(Day, id, 10);
        state.MarkWarningSent(Day, id, 10);
        state.Set("current_day", "2026-10-08");
        state.Set("current_day", "2026-10-09");
        state.Log("test", "Привет");

        Assert.Equal([(id, 10)], state.WarningsSent(Day));
        Assert.Empty(state.WarningsSent(Day.AddDays(1)));
        Assert.Equal("2026-10-09", state.Get("current_day"));
        Assert.Equal("Привет", Assert.Single(state.RecentEvents(10)).Message);
    }

    [Fact]
    public void Apps_keep_icon_when_updated_without_one()
    {
        var apps = new AppRepository(_db);
        apps.Upsert("game.exe", "Game", @"C:\Games\game.exe", [1, 2, 3], 100);
        apps.Upsert("game.exe", "Game 2", null, null, 200);
        apps.TouchLastSeen([new KeyValuePair<string, long>("game.exe", 300)]);

        var row = Assert.Single(apps.LoadAll());
        Assert.Equal("Game 2", row.DisplayName);
        Assert.Equal(@"C:\Games\game.exe", row.Path);
        Assert.Equal(1, row.HasIcon);
        Assert.Equal(300, row.LastSeenUtc);
        Assert.Equal(new byte[] { 1, 2, 3 }, apps.GetIcon("game.exe"));
    }
}
