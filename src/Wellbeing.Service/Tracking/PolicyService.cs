using Wellbeing.Core.Abstractions;
using Wellbeing.Core.Policies;
using Wellbeing.Core.Time;
using Wellbeing.Service.Data;

namespace Wellbeing.Service.Tracking;

public sealed record PolicyChangeOutcome(PolicyChangePlan Plan, Policy TodayBefore, Policy LatestBefore, DateTimeOffset TomorrowStartsAt)
{
    public ChangeTiming TimingFor(Guid limitId) => PolicyEditor.TimingFor(limitId, TodayBefore, LatestBefore, Plan);

    public ChangeTiming AccountingTiming => PolicyEditor.TimingForAccounting(TodayBefore, LatestBefore, Plan);
}

/// <summary>Политика лимитов: чтение на нужный день и изменения по правилу «ослабление — с завтра».</summary>
public sealed class PolicyService
{
    readonly Lock _gate = new();
    readonly PolicyRepository _repository;
    readonly IClock _clock;
    readonly ILogger<PolicyService> _log;
    readonly PolicyBook _book;

    public PolicyService(PolicyRepository repository, IClock clock, ILogger<PolicyService> log)
    {
        _repository = repository;
        _clock = clock;
        _log = log;
        _book = new PolicyBook(repository.LoadAll());
    }

    public event Action? Changed;

    public Policy ForDay(DateOnly day)
    {
        lock (_gate) return _book.ForDay(day);
    }

    /// <summary>Политика, которая будет действовать с завтра (с учётом всех запланированных изменений).</summary>
    public Policy Latest
    {
        get { lock (_gate) return _book.Latest; }
    }

    public DateTimeOffset TomorrowStartsAt(DateOnly today) =>
        _clock.FromLocal(DayClock.EndOf(today, ForDay(today).Accounting.DayResetTime));

    public DateTimeOffset DayStartsAt(DateOnly day) =>
        _clock.FromLocal(LogicalDay.Start(day, ForDay(day.AddDays(-1)).Accounting.DayResetTime));

    public PolicyChangeOutcome Change(DateOnly today, Func<Policy, Policy> edit, string summary, bool dryRun)
    {
        PolicyChangePlan plan;
        Policy todayBefore, latestBefore;
        lock (_gate)
        {
            todayBefore = _book.ForDay(today);
            latestBefore = _book.Latest;
            plan = _book.Plan(today, edit);
            if (!dryRun && plan.Timing != ChangeTiming.None)
            {
                var revisions = _book.CreateRevisions(plan, today, _clock.UtcNow.UtcDateTime, summary);
                _repository.Insert(revisions);
                _book.Append(revisions);
                _log.LogInformation("Политика изменена ({Timing}): {Summary}", plan.Timing, summary);
            }
        }

        if (!dryRun && plan.Timing != ChangeTiming.None) Changed?.Invoke();
        return new PolicyChangeOutcome(plan, todayBefore, latestBefore, TomorrowStartsAt(today));
    }
}
