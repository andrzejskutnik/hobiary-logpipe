namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Thread-safe liczniki operacyjne adaptera.
/// </summary>
public sealed class PipeMetrics
{
    private long _messagesReceived;
    private long _messagesWritten;
    private long _messagesRejected;
    private long _reconnects;
    private long _dlqMessages;
    private long _messagesFiltered;
    private long _messagesRateLimited;
    private long _messagesSchemaInvalid;
    private long _heartbeatsReceived;

    public void IncrementReceived() => Interlocked.Increment(ref _messagesReceived);
    public void IncrementWritten() => Interlocked.Increment(ref _messagesWritten);
    public void IncrementRejected() => Interlocked.Increment(ref _messagesRejected);
    public void IncrementReconnects() => Interlocked.Increment(ref _reconnects);
    public void IncrementDlq() => Interlocked.Increment(ref _dlqMessages);
    public void IncrementFiltered() => Interlocked.Increment(ref _messagesFiltered);
    public void IncrementRateLimited() => Interlocked.Increment(ref _messagesRateLimited);
    public void IncrementSchemaInvalid() => Interlocked.Increment(ref _messagesSchemaInvalid);
    public void IncrementHeartbeats() => Interlocked.Increment(ref _heartbeatsReceived);

    public PipeMetricsSnapshot GetSnapshot() => new(
        MessagesReceived: Interlocked.Read(ref _messagesReceived),
        MessagesWritten: Interlocked.Read(ref _messagesWritten),
        MessagesRejected: Interlocked.Read(ref _messagesRejected),
        Reconnects: Interlocked.Read(ref _reconnects),
        DlqMessages: Interlocked.Read(ref _dlqMessages),
        MessagesFiltered: Interlocked.Read(ref _messagesFiltered),
        MessagesRateLimited: Interlocked.Read(ref _messagesRateLimited),
        MessagesSchemaInvalid: Interlocked.Read(ref _messagesSchemaInvalid),
        HeartbeatsReceived: Interlocked.Read(ref _heartbeatsReceived));
}

public sealed record PipeMetricsSnapshot(
    long MessagesReceived,
    long MessagesWritten,
    long MessagesRejected,
    long Reconnects,
    long DlqMessages,
    long MessagesFiltered,
    long MessagesRateLimited,
    long MessagesSchemaInvalid,
    long HeartbeatsReceived);
