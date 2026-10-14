using Wellbeing.Core.Policies;

namespace Wellbeing.Core.Tests;

/// <summary>Правило «уменьшение и новые лимиты — сразу; увеличение и удаление — со следующего дня».</summary>
public class PolicyTests
{
    static readonly DateOnly Thursday = new(2026, 10, 8);
    static readonly DateOnly Friday = Thursday.AddDays(1);
    static readonly DateOnly Saturday = Thursday.AddDays(2);
    static readonly Guid Games = Guid.Parse("00000000-0000-0000-0000-000000000001");
    static readonly Guid Browser = Guid.Parse("00000000-0000-0000-0000-000000000002");

    /// <summary>Книга политики, в которой правки сразу «сохраняются», как это делает служба.</summary>
    sealed class Book
    {
        public PolicyBook Inner { get; } = new([]);
        public DateOnly Today { get; set; } = Thursday;

        public PolicyChangePlan Edit(Func<Policy, Policy> edit)
        {
            var plan = Inner.Plan(Today, edit);
            Inner.Append(Inner.CreateRevisions(plan, Today, DateTime.UtcNow, null));
            return plan;
        }

        public LimitRule? TodayLimit(Guid id) => Inner.ForDay(Today).Find(id);
        public LimitRule? TomorrowLimit(Guid id) => Inner.ForDay(Today.AddDays(1)).Find(id);
    }

    static Func<Policy, Policy> Upsert(LimitRule rule) => p => p with
    {
        Limits = p.Find(rule.Id) is null
            ? [.. p.Limits, rule]
            : p.Limits.Select(l => l.Id == rule.Id ? rule : l).ToList(),
    };

    static Func<Policy, Policy> Remove(Guid id) => p => p with { Limits = p.Limits.Where(l => l.Id != id).ToList() };

    static LimitRule GamesLimit(int weekday, int? weekend = null, params string[] exes) =>
        LimitRule.Create(Games, "Игры", exes.Length == 0 ? ["game.exe"] : exes, weekday, weekend);

    static Book WithGames(int weekday, int? weekend = null, params string[] exes)
    {
        var book = new Book();
        book.Edit(Upsert(GamesLimit(weekday, weekend, exes)));
        return book;
    }

    [Fact]
    public void New_limit_applies_immediately()
    {
        var book = new Book();
        var plan = book.Edit(Upsert(GamesLimit(60)));

        Assert.Equal(ChangeTiming.Immediate, plan.Timing);
        Assert.Equal(60, book.TodayLimit(Games)!.WeekdayMinutes);
    }

    [Fact]
    public void Decrease_applies_immediately()
    {
        var book = WithGames(60);
        var plan = book.Edit(Upsert(GamesLimit(30)));

        Assert.Equal(ChangeTiming.Immediate, plan.Timing);
        Assert.Equal(30, book.TodayLimit(Games)!.WeekdayMinutes);
        Assert.Equal(30, book.TomorrowLimit(Games)!.WeekdayMinutes);
    }

    [Fact]
    public void Increase_applies_only_from_tomorrow()
    {
        var book = WithGames(60);
        var plan = book.Edit(Upsert(GamesLimit(120)));

        Assert.Equal(ChangeTiming.Deferred, plan.Timing);
        Assert.Equal(60, book.TodayLimit(Games)!.WeekdayMinutes);
        Assert.Equal(120, book.TomorrowLimit(Games)!.WeekdayMinutes);

        book.Today = Friday;
        Assert.Equal(120, book.TodayLimit(Games)!.WeekdayMinutes);
    }

    [Fact]
    public void Delete_applies_only_from_tomorrow()
    {
        var book = WithGames(60);
        var plan = book.Edit(Remove(Games));

        Assert.Equal(ChangeTiming.Deferred, plan.Timing);
        Assert.NotNull(book.TodayLimit(Games));
        Assert.Null(book.TomorrowLimit(Games));
    }

    [Fact]
    public void Mixed_change_tightens_today_and_loosens_tomorrow()
    {
        // Четверг — будний день. Было 60/60, стало будни 30 / выходные 120.
        var book = WithGames(60);
        var plan = book.Edit(Upsert(GamesLimit(30, 120)));

        Assert.Equal(ChangeTiming.Partial, plan.Timing);
        var today = book.TodayLimit(Games)!;
        Assert.Equal(30, today.MinutesFor(Thursday));
        Assert.Equal(60, today.MinutesFor(Saturday)); // выходные пока не ослаблены

        var tomorrow = book.TomorrowLimit(Games)!;
        Assert.Equal(30, tomorrow.WeekdayMinutes);
        Assert.Equal(120, tomorrow.WeekendMinutes);
    }

    [Fact]
    public void Adding_program_to_group_applies_immediately()
    {
        var book = WithGames(60, null, "game.exe");
        var plan = book.Edit(Upsert(GamesLimit(60, null, "game.exe", "other.exe")));

        Assert.Equal(ChangeTiming.Immediate, plan.Timing);
        Assert.Contains("other.exe", book.TodayLimit(Games)!.Exes);
    }

    [Fact]
    public void Removing_program_from_group_applies_only_from_tomorrow()
    {
        var book = WithGames(60, null, "game.exe", "other.exe");
        var plan = book.Edit(Upsert(GamesLimit(60, null, "game.exe")));

        Assert.Equal(ChangeTiming.Deferred, plan.Timing);
        Assert.Contains("other.exe", book.TodayLimit(Games)!.Exes);
        Assert.DoesNotContain("other.exe", book.TomorrowLimit(Games)!.Exes);
    }

    [Fact]
    public void Decrease_after_pending_increase_wins_for_tomorrow_too()
    {
        var book = WithGames(60);
        book.Edit(Upsert(GamesLimit(120)));  // с завтра 120
        var plan = book.Edit(Upsert(GamesLimit(30)));

        Assert.Equal(ChangeTiming.Immediate, plan.Timing);
        Assert.Equal(30, book.TodayLimit(Games)!.WeekdayMinutes);
        Assert.Equal(30, book.TomorrowLimit(Games)!.WeekdayMinutes);
    }

    [Fact]
    public void Increase_after_decrease_keeps_today_tight()
    {
        var book = WithGames(60);
        book.Edit(Upsert(GamesLimit(30)));
        var plan = book.Edit(Upsert(GamesLimit(90)));

        Assert.Equal(ChangeTiming.Deferred, plan.Timing);
        Assert.Equal(30, book.TodayLimit(Games)!.WeekdayMinutes);
        Assert.Equal(90, book.TomorrowLimit(Games)!.WeekdayMinutes);
    }

    [Fact]
    public void Repeated_increases_never_affect_today()
    {
        var book = WithGames(60);
        book.Edit(Upsert(GamesLimit(90)));
        book.Edit(Upsert(GamesLimit(120)));
        book.Edit(Remove(Games));
        book.Edit(Upsert(GamesLimit(600)));

        Assert.Equal(60, book.TodayLimit(Games)!.WeekdayMinutes);
        Assert.Equal(600, book.TomorrowLimit(Games)!.WeekdayMinutes);
    }

    [Fact]
    public void Cancelling_pending_increase_restores_today_value_for_tomorrow()
    {
        var book = WithGames(60);
        book.Edit(Upsert(GamesLimit(120)));
        var todayVersion = book.TodayLimit(Games)!;

        var plan = book.Edit(Upsert(todayVersion));

        Assert.Equal(ChangeTiming.Immediate, plan.Timing);
        Assert.Equal(60, book.TomorrowLimit(Games)!.WeekdayMinutes);
    }

    [Fact]
    public void Rename_only_applies_immediately()
    {
        var book = WithGames(60);
        var plan = book.Edit(Upsert(GamesLimit(60) with { Name = "Игрушки" }));

        Assert.Equal(ChangeTiming.Immediate, plan.Timing);
        Assert.Equal("Игрушки", book.TodayLimit(Games)!.Name);
    }

    [Fact]
    public void Rename_with_increase_is_reported_as_deferred()
    {
        var book = WithGames(60);
        var plan = book.Edit(Upsert(GamesLimit(90) with { Name = "Игрушки" }));

        Assert.Equal(ChangeTiming.Deferred, plan.Timing);
        Assert.Equal(60, book.TodayLimit(Games)!.WeekdayMinutes);
    }

    [Fact]
    public void Unchanged_edit_is_noop()
    {
        var book = WithGames(60);
        var revisions = book.Inner.Revisions.Count;

        var plan = book.Edit(Upsert(GamesLimit(60)));

        Assert.Equal(ChangeTiming.None, plan.Timing);
        Assert.Equal(revisions, book.Inner.Revisions.Count);
    }

    [Fact]
    public void Changes_to_other_limits_do_not_unlock_pending_ones()
    {
        var book = WithGames(60);
        book.Edit(Remove(Games));                                     // удаление — с завтра
        var plan = book.Edit(Upsert(LimitRule.Create(Browser, "Браузер", ["chrome.exe"], 45)));

        Assert.Equal(ChangeTiming.Partial, plan.Timing);
        Assert.NotNull(book.TodayLimit(Games));                       // «Игры» сегодня всё ещё действуют
        Assert.NotNull(book.TodayLimit(Browser));                     // новый лимит — сразу
        Assert.Null(book.TomorrowLimit(Games));
        Assert.NotNull(book.TomorrowLimit(Browser));
    }

    [Fact]
    public void Idle_threshold_increase_is_immediate_and_decrease_is_deferred()
    {
        var book = new Book();
        var up = book.Edit(p => p with { Accounting = p.Accounting with { IdleThresholdSeconds = 300 } });
        Assert.Equal(ChangeTiming.Immediate, up.Timing);
        Assert.Equal(300, book.Inner.ForDay(Thursday).Accounting.IdleThresholdSeconds);

        var down = book.Edit(p => p with { Accounting = p.Accounting with { IdleThresholdSeconds = 60 } });
        Assert.Equal(ChangeTiming.Deferred, down.Timing);
        Assert.Equal(300, book.Inner.ForDay(Thursday).Accounting.IdleThresholdSeconds);
        Assert.Equal(60, book.Inner.ForDay(Friday).Accounting.IdleThresholdSeconds);
    }

    [Fact]
    public void Reset_time_change_is_deferred()
    {
        var book = new Book();
        var plan = book.Edit(p => p with { Accounting = p.Accounting with { DayResetTime = new TimeOnly(6, 0) } });

        Assert.Equal(ChangeTiming.Deferred, plan.Timing);
        Assert.Equal(new TimeOnly(4, 0), book.Inner.ForDay(Thursday).Accounting.DayResetTime);
        Assert.Equal(new TimeOnly(6, 0), book.Inner.ForDay(Friday).Accounting.DayResetTime);
    }

    [Fact]
    public void Diff_shows_pending_changes()
    {
        var book = WithGames(60);
        book.Edit(Upsert(LimitRule.Create(Browser, "Браузер", ["chrome.exe"], 45)));
        book.Edit(Upsert(GamesLimit(90)));
        book.Edit(Remove(Browser));

        var diff = PolicyBook.Diff(book.Inner.ForDay(Thursday), book.Inner.ForDay(Friday));

        var games = Assert.Single(diff, d => d.Id == Games);
        Assert.True(games.HasPendingChange);
        Assert.False(games.PendingDelete);
        Assert.Equal(90, games.Tomorrow!.WeekdayMinutes);

        var browser = Assert.Single(diff, d => d.Id == Browser);
        Assert.True(browser.PendingDelete);
    }

    ChangeTiming EditTiming(Book book, Guid id, Func<Policy, Policy> edit)
    {
        var todayBefore = book.Inner.ForDay(book.Today);
        var latestBefore = book.Inner.Latest;
        var plan = book.Edit(edit);
        return PolicyEditor.TimingFor(id, todayBefore, latestBefore, plan);
    }

    [Fact]
    public void New_limit_is_immediate_even_when_other_limit_has_pending_increase()
    {
        var book = WithGames(60);
        book.Edit(Upsert(GamesLimit(90)));

        var timing = EditTiming(book, Browser, Upsert(LimitRule.Create(Browser, "Браузер", ["chrome.exe"], 45)));

        Assert.Equal(ChangeTiming.Immediate, timing);
        Assert.Equal(60, book.TodayLimit(Games)!.WeekdayMinutes); // чужое ослабление по-прежнему ждёт завтра
    }

    [Theory]
    [InlineData(30, null, ChangeTiming.Immediate)]
    [InlineData(90, null, ChangeTiming.Deferred)]
    [InlineData(30, 120, ChangeTiming.Partial)]
    [InlineData(60, null, ChangeTiming.None)]
    public void Timing_of_single_limit_edit(int weekday, int? weekend, ChangeTiming expected)
    {
        var book = WithGames(60);
        Assert.Equal(expected, EditTiming(book, Games, Upsert(GamesLimit(weekday, weekend))));
    }

    [Fact]
    public void Edit_between_today_and_pending_value_is_deferred()
    {
        var book = WithGames(60);
        book.Edit(Upsert(GamesLimit(120)));

        Assert.Equal(ChangeTiming.Deferred, EditTiming(book, Games, Upsert(GamesLimit(90))));
        Assert.Equal(60, book.TodayLimit(Games)!.WeekdayMinutes);
        Assert.Equal(90, book.TomorrowLimit(Games)!.WeekdayMinutes);
    }

    [Fact]
    public void Delete_timing_is_deferred()
    {
        var book = WithGames(60);
        Assert.Equal(ChangeTiming.Deferred, EditTiming(book, Games, Remove(Games)));
    }

    [Fact]
    public void Policy_survives_json_round_trip()
    {
        var policy = new Policy
        {
            Limits = [GamesLimit(60, 120, "a.exe", "b.exe")],
            Accounting = new AccountingSettings(new TimeOnly(5, 30), 180),
        };

        var restored = PolicyJson.Deserialize(PolicyJson.Serialize(policy));

        Assert.True(PolicyMath.AreIdentical(policy, restored));
    }
}
