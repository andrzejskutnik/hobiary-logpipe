namespace HobiaryLogPipe.Adapter;

internal sealed class BatchAckTracker
{
    private readonly int _batchSize;
    private readonly int _timeoutMs;
    private ulong _lastDeliveryTag;
    private int _pendingCount;
    private long _lastFlushTicks;

    public bool HasPending => _pendingCount > 0;

    public BatchAckTracker(int batchSize, int timeoutMs)
    {
        _batchSize = batchSize;
        _timeoutMs = timeoutMs;
    }

    public BatchAckDecision? Record(ulong deliveryTag, long timestamp)
    {
        if (_batchSize <= 0)
            return new BatchAckDecision(deliveryTag, Multiple: false);

        _lastDeliveryTag = deliveryTag;
        _pendingCount++;

        var timeoutElapsed = _lastFlushTicks > 0
            && timestamp - _lastFlushTicks >= _timeoutMs;

        if (_pendingCount >= _batchSize || timeoutElapsed)
        {
            var decision = new BatchAckDecision(_lastDeliveryTag, Multiple: true);
            _pendingCount = 0;
            _lastFlushTicks = timestamp;
            return decision;
        }

        if (_lastFlushTicks == 0)
            _lastFlushTicks = timestamp;

        return null;
    }

    public void Reset()
    {
        _lastDeliveryTag = 0;
        _pendingCount = 0;
        _lastFlushTicks = 0;
    }

    public BatchAckDecision? FlushPending()
    {
        if (_pendingCount == 0)
            return null;

        var decision = new BatchAckDecision(_lastDeliveryTag, Multiple: true);
        Reset();
        return decision;
    }
}

internal sealed record BatchAckDecision(ulong DeliveryTag, bool Multiple);