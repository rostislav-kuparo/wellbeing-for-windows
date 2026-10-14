namespace Wellbeing.Core.Policies;

public sealed record PolicyRevision(long Seq, DateOnly EffectiveDay, Policy Policy, DateTime CreatedUtc, string? Summary);

/// <summary>Изменение одного лимита между сегодняшней и завтрашней политикой — для показа «с завтра» в UI.</summary>
public sealed record LimitChange(Guid Id, LimitRule? Today, LimitRule? Tomorrow)
{
    public bool HasPendingChange => !PolicyMath.AreIdentical(Today, Tomorrow);
    public bool PendingDelete => Today is not null && Tomorrow is null;
}

/// <summary>
/// История политики. Политика на день D — ревизия с наибольшим номером среди тех, что действуют с дня ≤ D.
/// Ревизии создаются только на сегодня или на завтра, поэтому политика на завтра всегда равна последней ревизии.
/// </summary>
public sealed class PolicyBook
{
    readonly List<PolicyRevision> _revisions;

    public PolicyBook(IEnumerable<PolicyRevision> revisions) =>
        _revisions = revisions.OrderBy(r => r.Seq).ToList();

    public IReadOnlyList<PolicyRevision> Revisions => _revisions;

    public Policy Latest => _revisions.Count == 0 ? Policy.Default : _revisions[^1].Policy;

    public Policy ForDay(DateOnly day)
    {
        for (var i = _revisions.Count - 1; i >= 0; i--)
            if (_revisions[i].EffectiveDay <= day)
                return _revisions[i].Policy;
        return Policy.Default;
    }

    public PolicyChangePlan Plan(DateOnly today, Func<Policy, Policy> edit) =>
        PolicyEditor.Plan(ForDay(today), Latest, edit(Latest));

    /// <summary>Ревизии для записи. В книгу они попадают через <see cref="Append"/> — после успешного сохранения.</summary>
    public IReadOnlyList<PolicyRevision> CreateRevisions(PolicyChangePlan plan, DateOnly today, DateTime nowUtc, string? summary)
    {
        var seq = _revisions.Count == 0 ? 1 : _revisions[^1].Seq + 1;
        var result = new List<PolicyRevision>(2);
        if (plan.ApplyToday is { } todayPolicy)
            result.Add(new PolicyRevision(seq++, today, todayPolicy, nowUtc, summary));
        if (plan.ApplyTomorrow is { } tomorrowPolicy)
            result.Add(new PolicyRevision(seq, today.AddDays(1), tomorrowPolicy, nowUtc, summary));
        return result;
    }

    public void Append(IEnumerable<PolicyRevision> revisions)
    {
        _revisions.AddRange(revisions);
        _revisions.Sort((a, b) => a.Seq.CompareTo(b.Seq));
    }

    public static IReadOnlyList<LimitChange> Diff(Policy today, Policy tomorrow)
    {
        var result = today.Limits.Select(t => new LimitChange(t.Id, t, tomorrow.Find(t.Id))).ToList();
        result.AddRange(tomorrow.Limits.Where(m => today.Find(m.Id) is null).Select(m => new LimitChange(m.Id, null, m)));
        return result;
    }
}
