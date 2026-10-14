using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Wellbeing.Service.Api;

public sealed record ServerEvent(string Name, string Json);

/// <summary>Рассылка событий подписчикам SSE (/api/events).</summary>
public sealed class EventBroadcaster
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly ConcurrentDictionary<Guid, Channel<ServerEvent>> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public (Guid Id, ChannelReader<ServerEvent> Reader) Subscribe()
    {
        var channel = Channel.CreateBounded<ServerEvent>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    public void Publish(string name, object payload)
    {
        if (_subscribers.IsEmpty) return;
        var message = new ServerEvent(name, JsonSerializer.Serialize(payload, Json));
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(message);
    }
}
