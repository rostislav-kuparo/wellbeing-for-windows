using Wellbeing.Core.Tracking;

namespace Wellbeing.Core.Tests;

public class AccountingEngineTests
{
    const int Idle2Min = 120;
    static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    static ActivitySample Active(string exe, int idle = 0, bool fullscreen = false, bool locked = false) =>
        new(exe, "title", idle, fullscreen, locked);

    static TickInput Input(ActivitySample? sample, double ageSeconds = 0.5, double elapsed = 1, Func<FallbackSnapshot>? fallback = null) =>
        new(TimeSpan.FromSeconds(elapsed), sample, TimeSpan.FromSeconds(ageSeconds), Idle2Min,
            fallback ?? (() => throw new InvalidOperationException("fallback не должен вызываться")));

    [Fact]
    public void Active_window_gets_the_second()
    {
        var result = new AccountingEngine().Tick(Input(Active("game.exe")));

        Assert.Equal(AttributionMode.Agent, result.Mode);
        Assert.True(result.IsCounting);
        Assert.Equal(1, result.Seconds);
        Assert.Equal(["game.exe"], result.Exes);
    }

    [Fact]
    public void Idle_longer_than_threshold_stops_time()
    {
        var result = new AccountingEngine().Tick(Input(Active("game.exe", idle: 121)));

        Assert.Equal(PauseReason.Idle, result.Pause);
        Assert.False(result.IsCounting);
        Assert.Equal(0, result.Seconds);
    }

    [Fact]
    public void Idle_just_below_threshold_still_counts()
    {
        var result = new AccountingEngine().Tick(Input(Active("game.exe", idle: 119)));
        Assert.True(result.IsCounting);
    }

    [Fact]
    public void Fullscreen_window_counts_even_when_idle()
    {
        var result = new AccountingEngine().Tick(Input(Active("vlc.exe", idle: 3600, fullscreen: true)));

        Assert.True(result.IsCounting);
        Assert.Equal(1, result.Seconds);
    }

    [Fact]
    public void Locked_session_does_not_count()
    {
        var result = new AccountingEngine().Tick(Input(Active("game.exe", locked: true)));
        Assert.Equal(PauseReason.Locked, result.Pause);
        Assert.False(result.IsCounting);
    }

    [Fact]
    public void No_foreground_window_does_not_count()
    {
        var result = new AccountingEngine().Tick(Input(new ActivitySample(null, null, 0, false, false)));
        Assert.Equal(PauseReason.NoWindow, result.Pause);
    }

    [Fact]
    public void Late_heartbeat_uses_last_known_window()
    {
        var result = new AccountingEngine().Tick(Input(Active("game.exe"), ageSeconds: 7));

        Assert.Equal(AttributionMode.AgentGrace, result.Mode);
        Assert.True(result.IsCounting);
    }

    [Fact]
    public void Without_agent_all_running_limited_programs_count()
    {
        var engine = new AccountingEngine();
        var result = engine.Tick(Input(Active("notepad.exe"), ageSeconds: 11,
            fallback: () => new FallbackSnapshot(true, false, ["game.exe", "steam.exe", "game.exe"])));

        Assert.Equal(AttributionMode.Fallback, result.Mode);
        Assert.Equal(1, result.Seconds);
        Assert.Equal(["game.exe", "steam.exe"], result.Exes);
    }

    [Fact]
    public void Without_agent_and_locked_session_nothing_counts()
    {
        var result = new AccountingEngine().Tick(Input(null, ageSeconds: 30,
            fallback: () => new FallbackSnapshot(true, true, ["game.exe"])));

        Assert.Equal(PauseReason.Locked, result.Pause);
        Assert.False(result.IsCounting);
    }

    [Fact]
    public void Without_logged_on_user_nothing_counts()
    {
        var result = new AccountingEngine().Tick(Input(null, ageSeconds: 30, fallback: () => FallbackSnapshot.NoUser));
        Assert.Equal(PauseReason.NoUser, result.Pause);
    }

    [Fact]
    public void Waits_for_first_heartbeat_after_start()
    {
        var result = new AccountingEngine().Tick(Input(null, ageSeconds: 4));
        Assert.Equal(PauseReason.WaitingForAgent, result.Pause);
    }

    [Fact]
    public void Sleep_gap_is_not_counted()
    {
        var result = new AccountingEngine().Tick(Input(Active("game.exe"), elapsed: 3600));

        Assert.Equal(PauseReason.SleepGap, result.Pause);
        Assert.Equal(0, result.Seconds);
    }

    [Fact]
    public void Uneven_ticks_add_up_to_whole_seconds()
    {
        var engine = new AccountingEngine();
        var total = 0;
        for (var i = 0; i < 10; i++)
            total += engine.Tick(Input(Active("game.exe"), elapsed: 0.6)).Seconds;

        Assert.Equal(6, total);
    }

    [Fact]
    public void One_minute_of_ticks_gives_sixty_seconds()
    {
        var engine = new AccountingEngine();
        var total = 0;
        for (var i = 0; i < 60; i++)
            total += engine.Tick(Input(Active("game.exe"), elapsed: OneSecond.TotalSeconds)).Seconds;

        Assert.Equal(60, total);
    }
}

public class UsageLedgerTests
{
    static readonly DateOnly Day = new(2026, 10, 8);
    static readonly DateTimeOffset Noon = new(2026, 10, 8, 12, 0, 30, TimeSpan.Zero);

    [Fact]
    public void Credits_go_to_day_totals_and_minute_buckets()
    {
        var ledger = new UsageLedger();
        ledger.StartDay(Day, new Dictionary<string, long> { ["game.exe"] = 100 });

        ledger.Credit(Day, Noon, ["game.exe"], 1);
        ledger.Credit(Day, Noon.AddSeconds(1), ["game.exe"], 1);
        ledger.Credit(Day, Noon.AddSeconds(40), ["game.exe", "steam.exe"], 1);

        Assert.Equal(103, ledger.SnapshotToday()["game.exe"]);
        Assert.Equal(1, ledger.SnapshotToday()["steam.exe"]);

        var pending = ledger.DrainPending().OrderBy(p => p.MinuteUtc).ThenBy(p => p.Exe).ToList();
        Assert.Equal(3, pending.Count);
        Assert.Equal(2, pending[0].Seconds);
        Assert.Equal(Noon.ToUnixTimeSeconds() / 60 * 60, pending[0].MinuteUtc);
        Assert.Empty(ledger.DrainPending());
    }

    [Fact]
    public void Restore_returns_failed_writes()
    {
        var ledger = new UsageLedger();
        ledger.StartDay(Day, new Dictionary<string, long>());
        ledger.Credit(Day, Noon, ["game.exe"], 5);

        var drained = ledger.DrainPending();
        ledger.Restore(drained);
        ledger.Credit(Day, Noon, ["game.exe"], 1);

        Assert.Equal(6, Assert.Single(ledger.DrainPending()).Seconds);
    }

    [Fact]
    public void New_day_starts_from_zero()
    {
        var ledger = new UsageLedger();
        ledger.StartDay(Day, new Dictionary<string, long>());
        ledger.Credit(Day, Noon, ["game.exe"], 5);

        ledger.StartDay(Day.AddDays(1), new Dictionary<string, long>());

        Assert.Empty(ledger.SnapshotToday());
    }
}
