using Wellbeing.Core.Time;

namespace Wellbeing.Core.Tests;

public class LogicalDayTests
{
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    static readonly TimeOnly Reset4 = new(4, 0);

    [Theory]
    [InlineData("2026-10-08 03:59", "2026-10-07")]
    [InlineData("2026-10-08 04:00", "2026-10-08")]
    [InlineData("2026-10-08 23:30", "2026-10-08")]
    [InlineData("2026-10-09 01:30", "2026-10-08")] // засиделся после полуночи — тот же день
    public void Day_starts_at_reset_time(string local, string expectedDay)
    {
        Assert.Equal(DateOnly.Parse(expectedDay, Inv), LogicalDay.Of(DateTime.Parse(local, Inv), Reset4));
    }

    [Fact]
    public void Midnight_reset_matches_calendar_date()
    {
        Assert.Equal(new DateOnly(2026, 10, 8), LogicalDay.Of(new DateTime(2026, 10, 8, 0, 0, 0), TimeOnly.MinValue));
    }

    [Theory]
    [InlineData("2026-10-10", true)] // суббота
    [InlineData("2026-10-11", true)] // воскресенье
    [InlineData("2026-10-12", false)]
    public void Weekend_detection(string day, bool weekend)
    {
        Assert.Equal(weekend, LogicalDay.IsWeekend(DateOnly.Parse(day, Inv)));
    }
}

public class DayClockTests
{
    static readonly DateOnly Oct8 = new(2026, 10, 8);
    static readonly TimeOnly Reset4 = new(4, 0);
    static TimeOnly Always4(DateOnly _) => Reset4;

    [Fact]
    public void Does_not_advance_before_reset()
    {
        var clock = new DayClock(Oct8);
        Assert.False(clock.Advance(new DateTime(2026, 10, 9, 3, 59, 59), Always4));
        Assert.Equal(Oct8, clock.CurrentDay);
    }

    [Fact]
    public void Advances_exactly_at_reset()
    {
        var clock = new DayClock(Oct8);
        Assert.True(clock.Advance(new DateTime(2026, 10, 9, 4, 0, 0), Always4));
        Assert.Equal(Oct8.AddDays(1), clock.CurrentDay);
    }

    [Fact]
    public void Never_goes_back_when_clock_moves_backwards()
    {
        var clock = new DayClock(Oct8);
        Assert.False(clock.Advance(new DateTime(2026, 10, 7, 12, 0, 0), Always4));
        Assert.Equal(Oct8, clock.CurrentDay);
    }

    [Fact]
    public void Jumps_over_days_when_computer_was_off()
    {
        var clock = new DayClock(Oct8);
        Assert.True(clock.Advance(new DateTime(2026, 10, 12, 10, 0, 0), Always4));
        Assert.Equal(new DateOnly(2026, 10, 12), clock.CurrentDay);
    }

    [Fact]
    public void Earlier_reset_time_set_today_does_not_end_today_early()
    {
        // Сегодня (8-е) сброс в 04:00; с завтра — в 02:00. День 8-го всё равно длится до 9-го 04:00.
        TimeOnly Reset(DateOnly d) => d <= Oct8 ? Reset4 : new TimeOnly(2, 0);
        var clock = new DayClock(Oct8);

        Assert.False(clock.Advance(new DateTime(2026, 10, 9, 3, 0, 0), Reset));
        Assert.True(clock.Advance(new DateTime(2026, 10, 9, 4, 0, 0), Reset));
        Assert.Equal(Oct8.AddDays(1), clock.CurrentDay);

        // А 9-е уже закончится по-новому — в 10-го 02:00.
        Assert.False(clock.Advance(new DateTime(2026, 10, 10, 1, 59, 0), Reset));
        Assert.True(clock.Advance(new DateTime(2026, 10, 10, 2, 0, 0), Reset));
        Assert.Equal(Oct8.AddDays(2), clock.CurrentDay);
    }

    [Fact]
    public void Later_reset_time_makes_next_day_longer()
    {
        // С завтра сброс в 06:00: 9-е начинается в 04:00 (граница по старым правилам), а заканчивается 10-го в 06:00.
        TimeOnly Reset(DateOnly d) => d <= Oct8 ? Reset4 : new TimeOnly(6, 0);
        var clock = new DayClock(Oct8);

        Assert.True(clock.Advance(new DateTime(2026, 10, 9, 5, 0, 0), Reset));
        Assert.Equal(Oct8.AddDays(1), clock.CurrentDay);
        Assert.False(clock.Advance(new DateTime(2026, 10, 10, 5, 0, 0), Reset));
        Assert.True(clock.Advance(new DateTime(2026, 10, 10, 6, 0, 0), Reset));
    }
}
