using System.Collections.Concurrent;
using Wellbeing.Core.Abstractions;
using Wellbeing.Core.Contracts;
using Wellbeing.Core.Tracking;

namespace Wellbeing.Service.Tracking;

/// <summary>Последний heartbeat агента в каждой сессии.</summary>
public sealed class AgentRegistry(IClock clock)
{
    public sealed record Entry(int AgentProcessId, ActivitySample Sample, TimeSpan ReceivedAt);

    readonly ConcurrentDictionary<int, Entry> _bySession = new();

    public void Record(int sessionId, int agentProcessId, ActivitySample sample) =>
        _bySession[sessionId] = new Entry(agentProcessId, sample, clock.Monotonic);

    public Entry? Get(int sessionId) => _bySession.GetValueOrDefault(sessionId);

    public TimeSpan? Age(int sessionId) => Get(sessionId) is { } entry ? clock.Monotonic - entry.ReceivedAt : null;

    public bool IsAlive(int sessionId) => Age(sessionId) is { } age && age <= AccountingEngine.AgentTimeout;
}

/// <summary>
/// Уведомления для пользователя. Если агент жив — уходят в ответе на heartbeat и показываются тостом.
/// Если агента нет — важные показываются системным окном сообщения (WTSSendMessage).
/// </summary>
public sealed class NotificationHub(AgentRegistry agents, IUserMessenger messenger, IClock clock, ILogger<NotificationHub> log)
{
    static readonly TimeSpan Expiry = TimeSpan.FromMinutes(1);

    sealed record Pending(AgentNotification Notification, TimeSpan CreatedAt);

    readonly ConcurrentDictionary<int, ConcurrentQueue<Pending>> _queues = new();

    public void Notify(int sessionId, string title, string message, bool important)
    {
        if (sessionId <= 0) return;
        if (agents.IsAlive(sessionId) || !important)
        {
            _queues.GetOrAdd(sessionId, _ => new ConcurrentQueue<Pending>())
                .Enqueue(new Pending(new AgentNotification(title, message, important), clock.Monotonic));
            return;
        }

        try
        {
            messenger.Show(sessionId, title, message);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Не удалось показать сообщение в сессии {Session}", sessionId);
        }
    }

    public IReadOnlyList<AgentNotification> Drain(int sessionId)
    {
        if (!_queues.TryGetValue(sessionId, out var queue)) return [];
        var now = clock.Monotonic;
        var result = new List<AgentNotification>();
        while (queue.TryDequeue(out var pending))
            if (now - pending.CreatedAt <= Expiry)
                result.Add(pending.Notification);
        return result;
    }
}
