using System.Text.Json;
using System.Threading.Channels;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Centralny hub SSE: każda przychodząca wiadomość jest emitowana do wszystkich aktywnych
/// subskrybentów. Subskrybenci rejestrują się tworząc ChannelReader przez <see cref="Subscribe"/>.
/// </summary>
public sealed class SseHub
{
    private readonly object _lock = new();
    private readonly List<Channel<string>> _subscribers = [];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Tworzy nowy kanał subskrybenta i rejestruje go w hub.</summary>
    public ChannelReader<string> Subscribe()
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        lock (_lock) _subscribers.Add(ch);
        return ch.Reader;
    }

    /// <summary>Usuwa kanał subskrybenta (wywołane gdy klient się rozłączy).</summary>
    public void Unsubscribe(ChannelReader<string> reader)
    {
        lock (_lock)
        {
            var ch = _subscribers.FirstOrDefault(c => c.Reader == reader);
            if (ch is not null)
            {
                ch.Writer.TryComplete();
                _subscribers.Remove(ch);
            }
        }
    }

    /// <summary>Emituje zdarzenie SSE do wszystkich subskrybentów.</summary>
    public void Emit(string pluginId, HobiaryLog.HlogLevel level, long timestampUs, string? payload)
    {
        if (_subscribers.Count == 0) return;

        var ev = JsonSerializer.Serialize(new SseLogEvent(
            pluginId,
            level.ToString().ToLowerInvariant(),
            timestampUs,
            payload), JsonOpts);

        List<Channel<string>> snapshot;
        lock (_lock) snapshot = [.. _subscribers];

        foreach (var ch in snapshot)
            ch.Writer.TryWrite(ev);
    }

    public int SubscriberCount { get { lock (_lock) return _subscribers.Count; } }

    /// <summary>Emituje zdarzenie zmiany stanu pluginu (online/offline) do wszystkich subskrybentów.</summary>
    public void EmitPluginStatus(string pluginId, bool online)
    {
        if (_subscribers.Count == 0) return;

        var ev = JsonSerializer.Serialize(new SsePluginStatusEvent(
            pluginId,
            online ? "plugin_online" : "plugin_offline",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L), JsonOpts);

        List<Channel<string>> snapshot;
        lock (_lock) snapshot = [.. _subscribers];
        foreach (var ch in snapshot)
            ch.Writer.TryWrite(ev);
    }

    /// <summary>Emituje zdarzenie wyrejestrowania pluginu do wszystkich subskrybentów.</summary>
    public void EmitPluginDeregistered(string pluginId)
    {
        if (_subscribers.Count == 0) return;

        var ev = JsonSerializer.Serialize(new SsePluginStatusEvent(
            pluginId,
            "plugin_deregistered",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L), JsonOpts);

        List<Channel<string>> snapshot;
        lock (_lock) snapshot = [.. _subscribers];
        foreach (var ch in snapshot)
            ch.Writer.TryWrite(ev);
    }
}

internal sealed record SseLogEvent(
    string PluginId,
    string Level,
    long Timestamp,
    string? Payload);

internal sealed record SsePluginStatusEvent(
    string PluginId,
    string EventType,
    long TimestampUs);
