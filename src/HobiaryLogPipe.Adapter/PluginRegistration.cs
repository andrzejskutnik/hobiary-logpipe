using HobiaryLogPipe.HobiaryLog;
using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.Adapter;

public sealed class PluginRegistration : IDisposable
{
    public string PluginId { get; init; } = string.Empty;
    public FieldMappingConfig FieldMapping { get; init; } = new();
    public string MessageFormat { get; init; } = "json";
    public HobiaryLogClient Client { get; init; } = null!;
    internal BatchWriter? BatchWriter { get; init; }
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;

    private long _lastHeartbeatTicks = 0;
    private long _deregistered = 0; // Interlocked: 0 = active, 1 = deregistered

    /// <summary>Aktualizuje znacznik ostatniego heartbeatu (thread-safe).</summary>
    public void TouchHeartbeat() =>
        Interlocked.Exchange(ref _lastHeartbeatTicks, DateTimeOffset.UtcNow.Ticks);

    /// <summary>Czas ostatniego heartbeatu lub null jeśli nie odebrano żadnego.</summary>
    public DateTimeOffset? LastHeartbeatAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastHeartbeatTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>True jeśli heartbeat był odebrany w ciągu ostatnich <paramref name="timeoutSeconds"/> sekund.</summary>
    public bool IsOnline(int timeoutSeconds)
    {
        if (timeoutSeconds <= 0) return true;
        var ticks = Interlocked.Read(ref _lastHeartbeatTicks);
        if (ticks == 0)
            return (DateTimeOffset.UtcNow - RegisteredAt) < TimeSpan.FromSeconds(timeoutSeconds);
        return (DateTimeOffset.UtcNow.Ticks - ticks) < TimeSpan.FromSeconds(timeoutSeconds).Ticks;
    }

    /// <summary>True jeśli plugin wysłał komunikat deregister i zakończył działanie.</summary>
    public bool IsDeregistered => Interlocked.Read(ref _deregistered) == 1L;

    /// <summary>Oznacza plugin jako wyrejestrowany (thread-safe).</summary>
    public void Deregister() => Interlocked.Exchange(ref _deregistered, 1L);

    public void Dispose()
    {
        BatchWriter?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Client?.Dispose();
    }
}
