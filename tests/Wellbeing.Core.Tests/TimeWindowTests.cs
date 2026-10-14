using Wellbeing.Core.Limits;
using Wellbeing.Core.Policies;

namespace Wellbeing.Core.Tests;

public class TimeWindowTests
{
    static TimeOnly T(int h, int m = 0) => new(h, m);
    static TimeWindow W(int fromH, int toH) => new(T(fromH), T(toH));

    [Theory]
    [InlineData(18, true)]
    [InlineData(21, true)]
    [InlineData(22, false)] // конец не включается
    [InlineData(17, false)]
    public void Evening_window(int hour, bool inside)
    {
        Assert.Equal(inside, W(18, 22).Contains(T(hour)));
    }

    [Theory]
    [InlineData(23, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(12, false)]
    public void Window_across_midnight(int hour, bool inside)
    {
        Assert.Equal(inside, W(20, 1).Contains(T(hour)));
    }

    [Fact]
    public void Seconds_until_end()
    {
        Assert.Equal(10 * 60, W(18, 22).SecondsUntilEnd(T(21, 50)));
        Assert.Equal(90 * 60, W(20, 1).SecondsUntilEnd(T(23, 30)));
        Assert.Equal(0, W(18, 22).SecondsUntilEnd(T(23)));
    }

    [Fact]
    public void Containment()
    {
        Assert.True(W(19, 21).IsWithin(W(18, 22)));
        Assert.True(W(18, 22).IsWithin(W(18, 22)));
        Assert.False(W(17, 21).IsWithin(W(18, 22)));
        Assert.True(W(23, 1).IsWithin(W(20, 2)));
        Assert.False(W(20, 2).IsWithin(W(23, 1)));
        Assert.True(TimeWindow.IsWithin(W(18, 22), null));  // окно строже, чем «весь день»
        Assert.False(TimeWindow.IsWithin(null, W(18, 22))); // убрать окно — ослабление
    }

    [Fact]
    public void Intersection()
    {
        Assert.Equal(W(19, 22), W(18, 22).Intersect(W(19, 23)));
        Assert.Equal(W(23, 1), W(20, 1).Intersect(W(23, 2)));
        Assert.True(W(8, 10).Intersect(W(18, 22)).IsEmpty);
        Assert.Equal(W(18, 22), TimeWindow.Intersect(null, W(18, 22)));
        Assert.Null(TimeWindow.Intersect(null, null));
    }
}

public class LimitWindowTests
{
    static readonly DateOnly Thursday = new(2026, 10, 8);
    static readonly DateOnly Saturday = new(2026, 10, 10);
    static readonly Guid Games = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    static readonly TimeWindow Evening = new(new TimeOnly(18, 0), new TimeOnly(22, 0));

    static LimitStatus Status(LimitRule rule, DateOnly day, int hour, int minute, long usedSeconds = 0) =>
        LimitEvaluator.Evaluate(Policy.Default with { Limits = [rule] }, day,
            new Dictionary<string, long> { ["game.exe"] = usedSeconds }, new TimeOnly(hour, minute))[0];

    [Fact]
    public void Outside_window_is_blocked_even_with_minutes_left()
    {
        var rule = LimitRule.Create(Games, "Игры", ["game.exe"], 120, weekdayWindow: Evening);

        var morning = Status(rule, Thursday, 10, 0);
        Assert.True(morning.IsBlocked);
        Assert.Equal(BlockReason.OutsideWindow, morning.BlockReason);
        Assert.Contains("game.exe", LimitEvaluator.BlockedExes([morning]).Keys);

        var evening = Status(rule, Thursday, 19, 0);
        Assert.False(evening.IsBlocked);
    }

    [Fact]
    public void Available_time_is_limited_by_window_end()
    {
        var rule = LimitRule.Create(Games, "Игры", ["game.exe"], 120, weekdayWindow: Evening);

        var status = Status(rule, Thursday, 21, 50, usedSeconds: 600);

        Assert.Equal(110 * 60, status.RemainingSeconds);
        Assert.Equal(10 * 60, status.AvailableSeconds);
    }

    [Fact]
    public void Exhausted_has_priority_over_window()
    {
        var rule = LimitRule.Create(Games, "Игры", ["game.exe"], 60, weekdayWindow: Evening);
        Assert.Equal(BlockReason.Exhausted, Status(rule, Thursday, 23, 0, usedSeconds: 3600).BlockReason);
    }

    [Fact]
    public void Weekend_uses_weekday_window_unless_separate()
    {
        var same = LimitRule.Create(Games, "Игры", ["game.exe"], 60, weekdayWindow: Evening);
        Assert.True(Status(same, Saturday, 10, 0).IsBlocked);

        var separate = LimitRule.Create(Games, "Игры", ["game.exe"], 60, 180, weekdayWindow: Evening, weekendWindow: null);
        Assert.False(Status(separate, Saturday, 10, 0).IsBlocked);
    }

    [Fact]
    public void Window_end_triggers_warnings()
    {
        var rule = LimitRule.Create(Games, "Игры", ["game.exe"], LimitRule.MaxMinutes, weekdayWindow: Evening);
        var status = Status(rule, Thursday, 21, 52);

        var warning = Assert.Single(WarningPlanner.Plan([status], ["game.exe"], [10, 5, 1], (_, _) => false));
        Assert.Equal([10], warning.Thresholds);
        Assert.Equal(8 * 60, warning.RemainingSeconds);
    }

    // ── правило «ослабление — с завтра» для окон ──

    static PolicyChangePlan Change(LimitRule from, LimitRule to)
    {
        var today = Policy.Default with { Limits = [from] };
        return PolicyEditor.Plan(today, today, Policy.Default with { Limits = [to] });
    }

    static LimitRule Rule(TimeWindow? window, int minutes = 60) =>
        LimitRule.Create(Games, "Игры", ["game.exe"], minutes, weekdayWindow: window);

    [Fact]
    public void Adding_window_is_immediate()
    {
        Assert.Equal(ChangeTiming.Immediate, Change(Rule(null), Rule(Evening)).Timing);
    }

    [Fact]
    public void Narrowing_window_is_immediate()
    {
        Assert.Equal(ChangeTiming.Immediate, Change(Rule(Evening), Rule(new TimeWindow(new(19, 0), new(21, 0)))).Timing);
    }

    [Fact]
    public void Removing_or_widening_window_is_deferred()
    {
        Assert.Equal(ChangeTiming.Deferred, Change(Rule(Evening), Rule(null)).Timing);

        var plan = Change(Rule(Evening), Rule(new TimeWindow(new(17, 0), new(23, 0))));
        Assert.Equal(ChangeTiming.Deferred, plan.Timing);
        Assert.Null(plan.ApplyToday); // сегодня остаётся 18–22
        Assert.Equal(new TimeWindow(new(17, 0), new(23, 0)), plan.ApplyTomorrow!.Limits[0].WeekdayWindow);
    }

    [Fact]
    public void Shifting_window_applies_overlap_today()
    {
        var plan = Change(Rule(Evening), Rule(new TimeWindow(new(20, 0), new(23, 0))));

        Assert.Equal(ChangeTiming.Partial, plan.Timing);
        Assert.Equal(new TimeWindow(new(20, 0), new(22, 0)), plan.ApplyToday!.Limits[0].WeekdayWindow);
        Assert.Equal(new TimeWindow(new(20, 0), new(23, 0)), plan.ApplyTomorrow!.Limits[0].WeekdayWindow);
    }

    [Fact]
    public void Window_survives_json_round_trip()
    {
        var policy = Policy.Default with { Limits = [LimitRule.Create(Games, "Игры", ["game.exe"], 60, 120, Evening, new TimeWindow(new(10, 0), new(23, 30)))] };
        var restored = PolicyJson.Deserialize(PolicyJson.Serialize(policy));
        Assert.True(PolicyMath.AreIdentical(policy, restored));
    }

    [Fact]
    public void Old_json_without_windows_still_loads()
    {
        const string json = """{"limits":[{"id":"00000000-0000-0000-0000-0000000000aa","name":"Игры","matchers":[{"type":"exe","exe":"game.exe"}],"weekdayMinutes":60,"weekendMinutes":null}],"accounting":{"dayResetTime":"04:00:00","idleThresholdSeconds":120}}""";
        var rule = PolicyJson.Deserialize(json).Limits[0];
        Assert.Null(rule.WeekdayWindow);
        Assert.Equal(60, rule.WeekdayMinutes);
    }
}
