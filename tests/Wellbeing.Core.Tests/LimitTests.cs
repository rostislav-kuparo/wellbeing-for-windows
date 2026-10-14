using Wellbeing.Core.Limits;
using Wellbeing.Core.Policies;

namespace Wellbeing.Core.Tests;

public class LimitEvaluatorTests
{
    static readonly DateOnly Thursday = new(2026, 10, 8);
    static readonly DateOnly Saturday = new(2026, 10, 10);

    static readonly TimeOnly Noon = new(12, 0);

    static Policy PolicyWith(params LimitRule[] limits) => Policy.Default with { Limits = limits };

    [Fact]
    public void Group_usage_is_sum_of_its_programs()
    {
        var group = LimitRule.Create(Guid.NewGuid(), "Игры", ["a.exe", "b.exe"], 60);
        var usage = new Dictionary<string, long> { ["a.exe"] = 1000, ["b.exe"] = 500, ["c.exe"] = 9999 };

        var status = Assert.Single(LimitEvaluator.Evaluate(PolicyWith(group), Thursday, usage, Noon));

        Assert.Equal(1500, status.UsedSeconds);
        Assert.Equal(3600, status.AllowedSeconds);
        Assert.Equal(2100, status.RemainingSeconds);
        Assert.False(status.IsExhausted);
    }

    [Fact]
    public void Weekend_limit_is_used_on_weekends()
    {
        var limit = LimitRule.Create(Guid.NewGuid(), "Игры", ["a.exe"], 60, 180);
        var usage = new Dictionary<string, long>();

        Assert.Equal(3600, LimitEvaluator.Evaluate(PolicyWith(limit), Thursday, usage, Noon)[0].AllowedSeconds);
        Assert.Equal(10800, LimitEvaluator.Evaluate(PolicyWith(limit), Saturday, usage, Noon)[0].AllowedSeconds);
    }

    [Fact]
    public void Exhausted_limit_blocks_all_its_programs()
    {
        var games = LimitRule.Create(Guid.NewGuid(), "Игры", ["a.exe", "b.exe"], 1);
        var browser = LimitRule.Create(Guid.NewGuid(), "Браузер", ["chrome.exe"], 60);
        var usage = new Dictionary<string, long> { ["a.exe"] = 60, ["chrome.exe"] = 10 };

        var blocked = LimitEvaluator.BlockedExes(LimitEvaluator.Evaluate(PolicyWith(games, browser), Thursday, usage, Noon));

        Assert.Equal(["a.exe", "b.exe"], blocked.Keys.Order());
        Assert.Equal("Игры", blocked["b.exe"].Rule.Name);
    }

    [Fact]
    public void Zero_minute_limit_blocks_from_the_start()
    {
        var limit = LimitRule.Create(Guid.NewGuid(), "Запрет", ["a.exe"], 0);
        var status = LimitEvaluator.Evaluate(PolicyWith(limit), Thursday, new Dictionary<string, long>(), Noon)[0];
        Assert.True(status.IsExhausted);
    }

    [Fact]
    public void Program_in_two_limits_is_blocked_by_either()
    {
        var single = LimitRule.Create(Guid.NewGuid(), "a", ["a.exe"], 10);
        var group = LimitRule.Create(Guid.NewGuid(), "Группа", ["a.exe", "b.exe"], 60);
        var usage = new Dictionary<string, long> { ["a.exe"] = 600 };

        var blocked = LimitEvaluator.BlockedExes(LimitEvaluator.Evaluate(PolicyWith(single, group), Thursday, usage, Noon));

        Assert.True(blocked.ContainsKey("a.exe"));
        Assert.False(blocked.ContainsKey("b.exe"));
    }
}

public class WarningPlannerTests
{
    static readonly int[] Thresholds = [10, 5, 1];
    static readonly LimitRule Games = LimitRule.Create(Guid.NewGuid(), "Игры", ["game.exe"], 60);

    static LimitStatus Remaining(long seconds) => new(Games, 3600, 3600 - seconds);

    [Fact]
    public void Warns_when_threshold_is_crossed_while_playing()
    {
        var warnings = WarningPlanner.Plan([Remaining(600)], ["game.exe"], Thresholds, (_, _) => false);

        var warning = Assert.Single(warnings);
        Assert.Equal([10], warning.Thresholds);
        Assert.Equal(600, warning.RemainingSeconds);
    }

    [Fact]
    public void No_warning_above_threshold()
    {
        Assert.Empty(WarningPlanner.Plan([Remaining(601)], ["game.exe"], Thresholds, (_, _) => false));
    }

    [Fact]
    public void Each_threshold_is_sent_once()
    {
        var sent = new HashSet<(Guid, int)> { (Games.Id, 10) };
        Assert.Empty(WarningPlanner.Plan([Remaining(500)], ["game.exe"], Thresholds, (id, t) => sent.Contains((id, t))));
    }

    [Fact]
    public void Several_crossed_thresholds_produce_one_warning()
    {
        var warning = Assert.Single(WarningPlanner.Plan([Remaining(50)], ["game.exe"], Thresholds, (_, _) => false));
        Assert.Equal([10, 5, 1], warning.Thresholds);
    }

    [Fact]
    public void Inactive_limits_do_not_warn()
    {
        Assert.Empty(WarningPlanner.Plan([Remaining(60)], ["chrome.exe"], Thresholds, (_, _) => false));
    }

    [Fact]
    public void Exhausted_limit_does_not_warn()
    {
        Assert.Empty(WarningPlanner.Plan([Remaining(0)], ["game.exe"], Thresholds, (_, _) => false));
    }
}
