using Wellbeing.Core.Agent;
using Wellbeing.Core.Policies;
using Wellbeing.Core.Safety;

namespace Wellbeing.Core.Tests;

public class ProtectionRulesTests
{
    static readonly ProtectionRules Rules = new([@"C:\Windows", @"C:\Program Files\Wellbeing\"]);
    static bool NotInWindows(string _) => false;

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"c:\windows\system32\notepad.exe")]
    [InlineData(@"\\?\C:\Windows\System32\svchost.exe")]
    [InlineData(@"C:\Program Files\Wellbeing\Wellbeing.Agent.exe")]
    public void System_and_own_paths_are_protected(string path)
    {
        Assert.True(Rules.IsProtectedPath(path));
    }

    [Theory]
    [InlineData(@"C:\Games\game.exe")]
    [InlineData(@"C:\WindowsApps\game.exe")]
    [InlineData(@"C:\Windows2\game.exe")]
    public void Other_paths_are_not_protected(string path)
    {
        Assert.False(Rules.IsProtectedPath(path));
    }

    [Fact]
    public void Session_zero_and_unknown_paths_are_never_terminated()
    {
        Assert.False(Rules.MayTerminate(0, "game.exe", @"C:\Games\game.exe"));
        Assert.False(Rules.MayTerminate(1, "game.exe", null));
        Assert.True(Rules.MayTerminate(1, "game.exe", @"C:\Games\game.exe"));
    }

    [Fact]
    public void Critical_names_are_never_terminated_even_outside_windows()
    {
        Assert.False(Rules.MayTerminate(1, "explorer.exe", @"C:\Games\explorer.exe"));
    }

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("svchost.exe")]
    [InlineData("wellbeing.agent.exe")]
    [InlineData("msedgewebview2.exe")]
    public void Critical_programs_cannot_be_limited(string exe)
    {
        Assert.NotNull(Rules.WhyCannotLimit(exe, null, NotInWindows));
    }

    [Fact]
    public void Program_that_exists_in_windows_cannot_be_limited()
    {
        Assert.NotNull(Rules.WhyCannotLimit("notepad.exe", null, exe => exe == "notepad.exe"));
    }

    [Fact]
    public void Ordinary_program_can_be_limited()
    {
        Assert.Null(Rules.WhyCannotLimit("game.exe", @"D:\Games\game.exe", NotInWindows));
    }

    [Fact]
    public void Validator_rejects_bad_limits()
    {
        var rule = LimitRule.Create(Guid.NewGuid(), "", ["explorer.exe"], 2000);

        var errors = PolicyValidator.Validate(rule, Rules, _ => null, NotInWindows);

        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Validator_accepts_good_limit()
    {
        var rule = LimitRule.Create(Guid.NewGuid(), "Игры", ["game.exe"], 60, 120);
        Assert.Empty(PolicyValidator.Validate(rule, Rules, _ => null, NotInWindows));
    }

    [Fact]
    public void Validator_rejects_title_rules_for_now()
    {
        var rule = LimitRule.Create(Guid.NewGuid(), "YouTube", ["chrome.exe"], 60) with
        {
            Matchers = [new Matcher(Matcher.TitleType, "chrome.exe", "YouTube")],
        };

        Assert.Single(PolicyValidator.Validate(rule, Rules, _ => null, NotInWindows));
    }

    [Theory]
    [InlineData(@"C:\Games\Game.EXE", "game.exe")]
    [InlineData("Steam.exe", "steam.exe")]
    [InlineData(@"D:/x/y/App.exe", "app.exe")]
    public void Exe_names_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, ExeName.Normalize(input));
    }
}

public class AgentWatchdogTests
{
    static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Healthy_agent_is_left_alone()
    {
        var watchdog = new AgentWatchdog(S(0));
        Assert.Equal(AgentAction.None, watchdog.Decide(S(100), S(99), true));
    }

    [Fact]
    public void Gives_autostart_agent_time_after_session_appears()
    {
        var watchdog = new AgentWatchdog(S(100));
        Assert.Equal(AgentAction.None, watchdog.Decide(S(105), null, false));
        Assert.Equal(AgentAction.Launch, watchdog.Decide(S(111), null, false));
    }

    [Fact]
    public void Silent_agent_process_is_restarted()
    {
        var watchdog = new AgentWatchdog(S(0));
        Assert.Equal(AgentAction.RestartHung, watchdog.Decide(S(100), S(85), true));
    }

    [Fact]
    public void Failed_launches_back_off()
    {
        var watchdog = new AgentWatchdog(S(0));
        Assert.Equal(AgentAction.Launch, watchdog.Decide(S(20), null, false));
        watchdog.OnLaunched(S(20));

        Assert.Equal(AgentAction.None, watchdog.Decide(S(25), null, false));   // ждём 10 с
        Assert.Equal(AgentAction.Launch, watchdog.Decide(S(31), null, false));
        watchdog.OnLaunched(S(31));

        Assert.Equal(AgentAction.None, watchdog.Decide(S(45), null, false));   // теперь 20 с
        Assert.Equal(AgentAction.Launch, watchdog.Decide(S(52), null, false));
        Assert.Equal(2, watchdog.FailedLaunches);
    }

    [Fact]
    public void Successful_launch_resets_backoff()
    {
        var watchdog = new AgentWatchdog(S(0));
        watchdog.Decide(S(20), null, false);
        watchdog.OnLaunched(S(20));
        watchdog.Decide(S(31), null, false);
        watchdog.OnLaunched(S(31));

        Assert.Equal(AgentAction.None, watchdog.Decide(S(33), S(32), true));
        Assert.Equal(0, watchdog.FailedLaunches);
    }
}
